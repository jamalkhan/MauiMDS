using Microsoft.Extensions.Logging;

namespace MauiMds.AudioCapture.MacCatalyst;

public sealed class MacAudioFormatConverter : IAudioFormatConverter
{
    private readonly IProcessRunner _processRunner;
    private readonly ILogger<MacAudioFormatConverter> _logger;

    public MacAudioFormatConverter(IProcessRunner processRunner, ILogger<MacAudioFormatConverter> logger)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    public Task<AudioCaptureResult> ConvertAsync(
        string sourcePath, string targetPath, TimeSpan duration, int bitRate = 128_000)
    {
        var ext = Path.GetExtension(targetPath).ToLowerInvariant();
        return ext switch
        {
            ".flac" => ConvertToFlacAsync(sourcePath, targetPath, duration),
            ".mp3"  => ConvertToMp3Async(sourcePath, targetPath, duration),
            _ => Task.FromResult(new AudioCaptureResult
                { Success = false, ErrorMessage = $"Unsupported recording format: {ext}" })
        };
    }

    private async Task<AudioCaptureResult> ConvertToFlacAsync(
        string sourcePath, string targetPath, TimeSpan duration)
    {
        var (exitCode, stderr) = await _processRunner.RunAsync(
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

        var (exitCode, stderr) = await _processRunner.RunAsync(
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
}
