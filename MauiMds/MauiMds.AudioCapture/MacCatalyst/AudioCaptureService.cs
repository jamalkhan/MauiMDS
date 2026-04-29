using AVFoundation;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace MauiMds.AudioCapture.MacCatalyst;

public sealed class AudioCaptureService : IAudioCaptureService, IDisposable
{
    private readonly ILogger<AudioCaptureService> _logger;
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    private AudioCaptureState _state = AudioCaptureState.Idle;
    private SystemAudioOutput? _systemAudio;
    private MicrophoneInput? _micAudio;
    private AudioFileWriter? _micWriter;
    private AudioFileWriter? _sysWriter;
    private AudioCaptureOptions? _activeOptions;

    // When the mic target format is not M4A, we record to temp M4A and convert after stop.
    private string? _desiredMicOutputPath;

    public AudioCaptureState State => _state;
    public string? LastStartWarning { get; private set; }
    public event EventHandler<AudioCaptureState>? StateChanged;

    public AudioCaptureService(ILogger<AudioCaptureService> logger)
    {
        _logger = logger;
    }

    public async Task<AudioPermissionStatus> CheckMicrophonePermissionAsync()
    {
        var status = AVCaptureDevice.GetAuthorizationStatus(AVAuthorizationMediaType.Audio);
        return status switch
        {
            AVAuthorizationStatus.Authorized => AudioPermissionStatus.Granted,
            AVAuthorizationStatus.Denied or AVAuthorizationStatus.Restricted => AudioPermissionStatus.Denied,
            _ => AudioPermissionStatus.NotDetermined
        };
    }

    public async Task<AudioPermissionStatus> RequestMicrophonePermissionAsync()
    {
        var granted = await AVCaptureDevice.RequestAccessForMediaTypeAsync(AVAuthorizationMediaType.Audio);
        return granted ? AudioPermissionStatus.Granted : AudioPermissionStatus.Denied;
    }

    public async Task StartAsync(AudioCaptureOptions options, CancellationToken cancellationToken = default)
    {
        if (!options.CaptureMicrophone && !options.CaptureSystemAudio)
            throw new ArgumentException("At least one audio source must be enabled.", nameof(options));

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (_state != AudioCaptureState.Idle)
                throw new InvalidOperationException($"Cannot start capture from state {_state}.");

            SetState(AudioCaptureState.Starting);
            LastStartWarning = null;
            _activeOptions = options;

            // Determine mic recording path — may need temp M4A if format is not M4A.
            string? internalMicM4aPath = null;
            if (options.CaptureMicrophone)
            {
                var targetExt = Path.GetExtension(options.OutputPath).ToLowerInvariant();
                if (targetExt == ".m4a")
                {
                    internalMicM4aPath = options.OutputPath;
                    _desiredMicOutputPath = null;
                }
                else
                {
                    _desiredMicOutputPath = options.OutputPath;
                    internalMicM4aPath = Path.ChangeExtension(options.OutputPath, ".tmp_mic.m4a");
                }
                EnsureDirectory(internalMicM4aPath);
            }

            if (!string.IsNullOrEmpty(options.SysOutputPath))
                EnsureDirectory(options.SysOutputPath);

            // Start sources — system audio can fail gracefully when mic is also enabled.
            var sysActive = false;
            if (options.CaptureSystemAudio)
            {
                _systemAudio = new SystemAudioOutput(_logger);
                try
                {
                    await _systemAudio.StartAsync(options.SampleRate, options.ChannelCount);
                    sysActive = true;
                }
                catch (InvalidOperationException ex) when (options.CaptureMicrophone)
                {
                    _logger.LogWarning(ex, "AudioCaptureService: system audio unavailable, falling back to microphone-only.");
                    _systemAudio.Dispose();
                    _systemAudio = null;
                    LastStartWarning =
                        "System audio could not be captured (Screen Recording permission may need an app restart). " +
                        "Recording microphone only.";
                }
            }

            var micActive = false;
            if (options.CaptureMicrophone)
            {
                _micAudio = new MicrophoneInput(_logger);
                _micAudio.Start();
                micActive = true;
            }

            // Create writers only for sources that actually started.
            if (micActive)
            {
                _micWriter = new AudioFileWriter(internalMicM4aPath!, options, "mic", _logger);
                _micAudio!.SampleBufferReceived += buf => _micWriter?.AppendBuffer(buf);
            }

            if (sysActive)
            {
                _sysWriter = new AudioFileWriter(options.SysOutputPath, options, "sys", _logger);
                _systemAudio!.SampleBufferReceived += buf => _sysWriter?.AppendBuffer(buf);
            }

            SetState(AudioCaptureState.Recording);
            _logger.LogInformation("AudioCaptureService: recording started. Mic={MicPath}, Sys={SysPath}",
                internalMicM4aPath ?? "(none)", options.SysOutputPath.Length > 0 ? options.SysOutputPath : "(none)");
        }
        catch
        {
            await CleanupSourcesAsync();
            SetState(AudioCaptureState.Idle);
            throw;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task<AudioCaptureResult> StopAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            if (_state != AudioCaptureState.Recording)
            {
                return new AudioCaptureResult
                {
                    Success = false,
                    ErrorMessage = $"Cannot stop — current state is {_state}."
                };
            }

            SetState(AudioCaptureState.Stopping);
            await CleanupSourcesAsync();

            var allPaths = new List<string>(2);
            var duration = TimeSpan.Zero;
            string? firstError = null;

            if (_micWriter is not null)
            {
                var micResult = await _micWriter.FinishAsync();
                _micWriter.Dispose();
                _micWriter = null;

                if (micResult.Success)
                {
                    // Convert mic file to desired format if needed.
                    var finalMicResult = await MaybeConvertMicAsync(micResult);
                    if (finalMicResult.Success && finalMicResult.FilePath.Length > 0)
                    {
                        allPaths.Add(finalMicResult.FilePath);
                        duration = finalMicResult.Duration;
                    }
                    else
                    {
                        firstError ??= finalMicResult.ErrorMessage;
                    }
                }
                else
                {
                    firstError ??= micResult.ErrorMessage;
                }
            }

            if (_sysWriter is not null)
            {
                var sysResult = await _sysWriter.FinishAsync();
                _sysWriter.Dispose();
                _sysWriter = null;

                if (sysResult.Success && sysResult.FilePath.Length > 0)
                {
                    allPaths.Add(sysResult.FilePath);
                    if (duration == TimeSpan.Zero)
                        duration = sysResult.Duration;
                }
                else
                {
                    firstError ??= sysResult.ErrorMessage;
                }
            }

            _desiredMicOutputPath = null;
            _activeOptions = null;
            SetState(AudioCaptureState.Idle);

            if (allPaths.Count == 0)
            {
                _logger.LogWarning("AudioCaptureService: stop produced no output files. Error: {Error}", firstError);
                return new AudioCaptureResult { Success = false, ErrorMessage = firstError ?? "No audio was captured." };
            }

            _logger.LogInformation("AudioCaptureService: recording stopped. Files={Count}, Duration={Duration:g}",
                allPaths.Count, duration);
            return new AudioCaptureResult { Success = true, AudioFilePaths = allPaths, Duration = duration };
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private async Task<AudioCaptureResult> MaybeConvertMicAsync(AudioCaptureResult micResult)
    {
        if (_desiredMicOutputPath is null)
            return micResult;

        var tempM4aPath = micResult.FilePath;
        var convertedResult = await ConvertToTargetFormatAsync(tempM4aPath, _desiredMicOutputPath, micResult.Duration);

        if (convertedResult.Success)
        {
            TryDeleteFile(tempM4aPath);
            _logger.LogInformation("AudioCaptureService: converted mic to {Ext}. Path={Path}",
                Path.GetExtension(_desiredMicOutputPath), _desiredMicOutputPath);
            return convertedResult;
        }

        _logger.LogWarning("AudioCaptureService: format conversion failed ({Err}) — keeping temp M4A at {Path}",
            convertedResult.ErrorMessage, tempM4aPath);

        // Return temp M4A as a fallback so the recording is not lost.
        return new AudioCaptureResult
        {
            Success = true,
            AudioFilePaths = [tempM4aPath],
            Duration = micResult.Duration
        };
    }

    private async Task<AudioCaptureResult> ConvertToTargetFormatAsync(
        string sourcePath, string targetPath, TimeSpan duration)
    {
        var ext = Path.GetExtension(targetPath).ToLowerInvariant();
        return ext switch
        {
            ".flac" => await ConvertToFlacAsync(sourcePath, targetPath, duration),
            ".mp3"  => await ConvertToMp3Async(sourcePath, targetPath, duration),
            _ => new AudioCaptureResult { Success = false, ErrorMessage = $"Unsupported recording format: {ext}" }
        };
    }

    private async Task<AudioCaptureResult> ConvertToFlacAsync(
        string sourcePath, string targetPath, TimeSpan duration)
    {
        var (exitCode, stderr) = await RunProcessAsync(
            "/usr/bin/afconvert",
            $"-f fLaC -d fLaC \"{sourcePath}\" \"{targetPath}\"");

        if (exitCode != 0 || !File.Exists(targetPath))
        {
            _logger.LogError("afconvert FLAC failed (code {Code}): {Err}", exitCode, stderr);
            return new AudioCaptureResult
            {
                Success = false,
                ErrorMessage = $"FLAC conversion failed (afconvert exited {exitCode}). {stderr}".Trim()
            };
        }
        return new AudioCaptureResult { Success = true, AudioFilePaths = [targetPath], Duration = duration };
    }

    private async Task<AudioCaptureResult> ConvertToMp3Async(
        string sourcePath, string targetPath, TimeSpan duration)
    {
        var ffmpeg = FindFfmpeg();
        if (ffmpeg is null)
        {
            _logger.LogWarning("ffmpeg not found — cannot convert to MP3.");
            return new AudioCaptureResult
            {
                Success = false,
                ErrorMessage = "MP3 recording requires ffmpeg. Install it with: brew install ffmpeg"
            };
        }

        var (exitCode, stderr) = await RunProcessAsync(
            ffmpeg, $"-i \"{sourcePath}\" -q:a 2 -y \"{targetPath}\"");

        if (exitCode != 0 || !File.Exists(targetPath))
        {
            _logger.LogError("ffmpeg MP3 failed (code {Code}): {Err}", exitCode, stderr);
            return new AudioCaptureResult
            {
                Success = false,
                ErrorMessage = $"MP3 conversion failed (ffmpeg exited {exitCode}). {stderr}".Trim()
            };
        }
        return new AudioCaptureResult { Success = true, AudioFilePaths = [targetPath], Duration = duration };
    }

    private static string? FindFfmpeg()
    {
        string[] candidates = ["/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg", "/usr/bin/ffmpeg"];
        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task<(int ExitCode, string Stderr)> RunProcessAsync(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = new Process { StartInfo = psi };
        process.Start();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, stderr);
    }

    private async Task CleanupSourcesAsync()
    {
        if (_systemAudio is not null)
        {
            await _systemAudio.StopAsync();
            _systemAudio.Dispose();
            _systemAudio = null;
        }
        _micAudio?.Stop();
        _micAudio?.Dispose();
        _micAudio = null;
    }

    private void SetState(AudioCaptureState newState)
    {
        _state = newState;
        StateChanged?.Invoke(this, newState);
    }

    private static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
    }

    private static void TryDeleteFile(string? path)
    {
        if (path is not null)
            try { File.Delete(path); } catch { /* best-effort */ }
    }

    public void Dispose()
    {
        _systemAudio?.Dispose();
        _micAudio?.Dispose();
        _micWriter?.Dispose();
        _sysWriter?.Dispose();
        _stateLock.Dispose();
    }
}
