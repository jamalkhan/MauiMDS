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
    private AudioFileWriter? _writer;
    private AudioCaptureOptions? _activeOptions;
    // When the target format is not M4A, we record to a temp M4A and convert after stop.
    private string? _desiredOutputPath;

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
        ArgumentException.ThrowIfNullOrWhiteSpace(options.OutputPath);

        if (!options.CaptureSystemAudio && !options.CaptureMicrophone)
        {
            throw new ArgumentException("At least one audio source must be enabled.", nameof(options));
        }

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (_state != AudioCaptureState.Idle)
            {
                throw new InvalidOperationException($"Cannot start capture from state {_state}.");
            }

            SetState(AudioCaptureState.Starting);
            LastStartWarning = null;
            _activeOptions = options;

            // If the desired format is not M4A, record to a temp M4A and convert on stop.
            var targetExt = Path.GetExtension(options.OutputPath).ToLowerInvariant();
            string internalM4aPath;
            if (targetExt == ".m4a")
            {
                internalM4aPath = options.OutputPath;
                _desiredOutputPath = null;
            }
            else
            {
                _desiredOutputPath = options.OutputPath;
                internalM4aPath = Path.Combine(
                    Path.GetDirectoryName(options.OutputPath)!,
                    Path.GetFileNameWithoutExtension(options.OutputPath) + ".tmp_rec.m4a");
            }

            // Ensure output directory exists.
            var dir = Path.GetDirectoryName(internalM4aPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            // Start sources first so we know which are actually active before
            // creating the writer (avoids ghost tracks for failed sources).
            var systemAudioActive = false;
            if (options.CaptureSystemAudio)
            {
                _systemAudio = new SystemAudioOutput(_logger);
                try
                {
                    await _systemAudio.StartAsync(options.SampleRate, options.ChannelCount);
                    systemAudioActive = true;
                }
                catch (InvalidOperationException ex) when (options.CaptureMicrophone)
                {
                    _logger.LogWarning(ex, "AudioCaptureService: system audio unavailable, falling back to microphone-only.");
                    _systemAudio.Dispose();
                    _systemAudio = null;
                    LastStartWarning = "System audio could not be captured (Screen Recording permission may need an app restart). " +
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

            // Create the writer with only the sources that are actually running.
            var effectiveOptions = new AudioCaptureOptions
            {
                OutputPath = internalM4aPath,
                SampleRate = options.SampleRate,
                ChannelCount = options.ChannelCount,
                EncoderBitRate = options.EncoderBitRate,
                CaptureSystemAudio = systemAudioActive,
                CaptureMicrophone = micActive,
            };
            _writer = new AudioFileWriter(effectiveOptions, _logger);

            if (systemAudioActive)
                _systemAudio!.SampleBufferReceived += buf => _writer?.AppendSystemAudio(buf);

            if (micActive)
                _micAudio!.SampleBufferReceived += buf => _writer?.AppendMicAudio(buf);

            SetState(AudioCaptureState.Recording);
            _logger.LogInformation("AudioCaptureService: recording started → {Path}", internalM4aPath);
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

            var result = _writer is not null
                ? await _writer.FinishAsync()
                : new AudioCaptureResult { Success = false, ErrorMessage = "Writer was not initialised." };

            _writer?.Dispose();
            _writer = null;

            // If we recorded to a temp M4A, convert to the desired format.
            if (result.Success && _desiredOutputPath is not null && result.FilePath is not null)
            {
                var convertedResult = await ConvertToTargetFormatAsync(
                    result.FilePath, _desiredOutputPath, result.Duration);

                if (convertedResult.Success)
                {
                    TryDeleteFile(result.FilePath);
                    result = convertedResult;
                    _logger.LogInformation("AudioCaptureService: converted to {Ext}. Path={Path}",
                        Path.GetExtension(_desiredOutputPath), _desiredOutputPath);
                }
                else
                {
                    _logger.LogWarning(
                        "AudioCaptureService: format conversion failed ({Err}) — keeping M4A at {Path}",
                        convertedResult.ErrorMessage, result.FilePath);
                }
            }

            _desiredOutputPath = null;
            _activeOptions = null;

            SetState(AudioCaptureState.Idle);
            _logger.LogInformation("AudioCaptureService: recording stopped. Success={Success}", result.Success);
            return result;
        }
        finally
        {
            _stateLock.Release();
        }
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
        // afconvert is always present on macOS.
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

        return new AudioCaptureResult { Success = true, FilePath = targetPath, Duration = duration };
    }

    private async Task<AudioCaptureResult> ConvertToMp3Async(
        string sourcePath, string targetPath, TimeSpan duration)
    {
        var ffmpeg = FindFfmpeg();
        if (ffmpeg is null)
        {
            _logger.LogWarning("ffmpeg not found — cannot convert to MP3. Install via 'brew install ffmpeg'.");
            return new AudioCaptureResult
            {
                Success = false,
                ErrorMessage = "MP3 recording requires ffmpeg. Install it with: brew install ffmpeg"
            };
        }

        var (exitCode, stderr) = await RunProcessAsync(
            ffmpeg,
            $"-i \"{sourcePath}\" -q:a 2 -y \"{targetPath}\"");

        if (exitCode != 0 || !File.Exists(targetPath))
        {
            _logger.LogError("ffmpeg MP3 failed (code {Code}): {Err}", exitCode, stderr);
            return new AudioCaptureResult
            {
                Success = false,
                ErrorMessage = $"MP3 conversion failed (ffmpeg exited {exitCode}). {stderr}".Trim()
            };
        }

        return new AudioCaptureResult { Success = true, FilePath = targetPath, Duration = duration };
    }

    private static string? FindFfmpeg()
    {
        string[] candidates = ["/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg", "/usr/bin/ffmpeg"];
        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task<(int ExitCode, string Stderr)> RunProcessAsync(
        string fileName, string arguments)
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

    private static void TryDeleteFile(string? path)
    {
        if (path is not null)
            try { File.Delete(path); } catch { /* best-effort */ }
    }

    public void Dispose()
    {
        _systemAudio?.Dispose();
        _micAudio?.Dispose();
        _writer?.Dispose();
        _stateLock.Dispose();
    }
}
