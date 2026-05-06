using AVFoundation;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace MauiMds.AudioCapture.MacCatalyst;

public sealed class AudioCaptureService : AudioCaptureServiceBase, INativeMicrophoneSource
{
    private SystemAudioOutput? _systemAudio;
    private MicrophoneInput? _micAudio;
    private AudioFileWriter? _micWriter;
    private AudioFileWriter? _sysWriter;
    private LiveAudioChunkWriter? _liveChunkWriter;
    private AudioCaptureOptions? _activeOptions;
    private bool _micPermissionGrantedThisSession;

    // When the mic target format is not M4A, we record to temp M4A and convert after stop.
    private string? _desiredMicOutputPath;

    // INativeMicrophoneSource — fires CMSampleBuffer.Handle for each mic buffer.
    public event EventHandler<nint>? NativeSampleBufferAvailable;

    public AudioCaptureService(ILogger<AudioCaptureService> logger) : base(logger) { }

    // ── Permissions ───────────────────────────────────────────────────────────

    public override Task<AudioPermissionStatus> CheckMicrophonePermissionAsync()
    {
        if (_micPermissionGrantedThisSession)
            return Task.FromResult(AudioPermissionStatus.Granted);

        var status = AVCaptureDevice.GetAuthorizationStatus(AVAuthorizationMediaType.Audio);
        return Task.FromResult(status switch
        {
            AVAuthorizationStatus.Authorized => AudioPermissionStatus.Granted,
            AVAuthorizationStatus.Denied or AVAuthorizationStatus.Restricted => AudioPermissionStatus.Denied,
            _ => AudioPermissionStatus.NotDetermined
        });
    }

    public override async Task<AudioPermissionStatus> RequestMicrophonePermissionAsync()
    {
        if (_micPermissionGrantedThisSession)
            return AudioPermissionStatus.Granted;

        var granted = await AVCaptureDevice.RequestAccessForMediaTypeAsync(AVAuthorizationMediaType.Audio);
        if (granted) _micPermissionGrantedThisSession = true;
        return granted ? AudioPermissionStatus.Granted : AudioPermissionStatus.Denied;
    }

    // ── Platform hooks ────────────────────────────────────────────────────────

    protected override async Task StartCaptureAsync(AudioCaptureOptions options, CancellationToken cancellationToken)
    {
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
            _systemAudio = new SystemAudioOutput(Logger);
            try
            {
                await _systemAudio.StartAsync(options.SampleRate, options.ChannelCount);
                sysActive = true;
            }
            catch (InvalidOperationException ex) when (options.CaptureMicrophone)
            {
                Logger.LogWarning(ex, "AudioCaptureService: system audio unavailable, falling back to microphone-only.");
                _systemAudio.Dispose();
                _systemAudio = null;
                LastStartWarning = "screen_recording_denied";
            }
        }

        var micActive = false;
        if (options.CaptureMicrophone)
        {
            _micAudio = new MicrophoneInput(Logger);
            _micAudio.Start();
            micActive = true;
        }

        // Create writers only for sources that actually started.
        if (micActive)
        {
            _micWriter = new AudioFileWriter(internalMicM4aPath!, options, "mic", Logger);
            _micAudio!.SampleBufferReceived += buf => _micWriter?.AppendBuffer(buf);

            // Live chunk writer — active only when requested.
            if (options.EnableLiveChunks)
            {
                _liveChunkWriter = new LiveAudioChunkWriter(options, Logger);
                _liveChunkWriter.ChunkReady += (_, chunk) => RaiseLiveChunkAvailable(chunk);
                _micAudio.SampleBufferReceived += buf => _liveChunkWriter?.AppendBuffer(buf);
            }

            // Native sample buffer forwarding for engines that can use raw buffers directly.
            if (NativeSampleBufferAvailable is not null)
                _micAudio.SampleBufferReceived += buf => NativeSampleBufferAvailable?.Invoke(this, buf.Handle);
        }

        if (sysActive)
        {
            _sysWriter = new AudioFileWriter(options.SysOutputPath, options, "sys", Logger);
            _systemAudio!.SampleBufferReceived += buf => _sysWriter?.AppendBuffer(buf);
        }

        Logger.LogInformation("AudioCaptureService: recording started. Mic={MicPath}, Sys={SysPath}",
            internalMicM4aPath ?? "(none)", options.SysOutputPath.Length > 0 ? options.SysOutputPath : "(none)");
    }

    protected override Task CleanupAfterFailedStartAsync() => CleanupSourcesAsync();

    protected override async Task<AudioCaptureResult> StopCaptureAsync()
    {
        // Flush the live chunk writer before stopping sources so the final chunk is emitted.
        if (_liveChunkWriter is not null)
        {
            await _liveChunkWriter.FlushAsync();
            _liveChunkWriter.Dispose();
            _liveChunkWriter = null;
        }

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

        if (allPaths.Count == 0)
        {
            Logger.LogWarning("AudioCaptureService: stop produced no output files. Error: {Error}", firstError);
            return new AudioCaptureResult { Success = false, ErrorMessage = firstError ?? "No audio was captured." };
        }

        Logger.LogInformation("AudioCaptureService: recording stopped. Files={Count}, Duration={Duration:g}",
            allPaths.Count, duration);
        return new AudioCaptureResult { Success = true, AudioFilePaths = allPaths, Duration = duration };
    }

    protected override void DisposeResources()
    {
        _systemAudio?.Dispose();
        _micAudio?.Dispose();
        _micWriter?.Dispose();
        _sysWriter?.Dispose();
        _liveChunkWriter?.Dispose();
    }

    // ── Mac-specific helpers ──────────────────────────────────────────────────

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

    private async Task<AudioCaptureResult> MaybeConvertMicAsync(AudioCaptureResult micResult)
    {
        if (_desiredMicOutputPath is null)
            return micResult;

        var tempM4aPath = micResult.FilePath;
        var convertedResult = await ConvertToTargetFormatAsync(tempM4aPath, _desiredMicOutputPath, micResult.Duration);

        if (convertedResult.Success)
        {
            TryDeleteFile(tempM4aPath);
            Logger.LogInformation("AudioCaptureService: converted mic to {Ext}. Path={Path}",
                Path.GetExtension(_desiredMicOutputPath), _desiredMicOutputPath);
            return convertedResult;
        }

        Logger.LogWarning("AudioCaptureService: format conversion failed ({Err}) — keeping temp M4A at {Path}",
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
            Logger.LogError("afconvert FLAC failed (code {Code}): {Err}", exitCode, stderr);
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
            Logger.LogWarning("ffmpeg not found — cannot convert to MP3.");
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
            Logger.LogError("ffmpeg MP3 failed (code {Code}): {Err}", exitCode, stderr);
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
}
