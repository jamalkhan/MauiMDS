#pragma warning disable CA1416
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace MauiMds.AudioCapture.Windows;

public sealed class WindowsAudioFormatConverter : IAudioFormatConverter
{
    private static readonly Guid Mp3SubType = new("00000055-0000-0010-8000-00aa00389b71");

    private readonly IProcessRunner _processRunner;
    private readonly ILogger<WindowsAudioFormatConverter> _logger;

    public WindowsAudioFormatConverter(
        IProcessRunner processRunner, ILogger<WindowsAudioFormatConverter> logger)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task<AudioCaptureResult> ConvertAsync(
        string sourcePath, string targetPath, TimeSpan duration, int bitRate = 128_000)
    {
        var ext = Path.GetExtension(targetPath).ToLowerInvariant();
        try
        {
            switch (ext)
            {
                case ".mp3":
                    if (TryEncodeToMp3WithMediaFoundation(sourcePath, targetPath, bitRate))
                    {
                        TryDeleteFile(sourcePath);
                        _logger.LogInformation("AudioFormatConverter: MP3 encoded via MediaFoundation.");
                        return new AudioCaptureResult { Success = true, AudioFilePaths = [targetPath], Duration = duration };
                    }
                    EncodeToMp3WithLame(sourcePath, targetPath, bitRate);
                    TryDeleteFile(sourcePath);
                    _logger.LogInformation("AudioFormatConverter: MP3 encoded via NAudio.Lame.");
                    return new AudioCaptureResult { Success = true, AudioFilePaths = [targetPath], Duration = duration };

                case ".flac":
                    if (await TryEncodeToFlacAsync(sourcePath, targetPath))
                    {
                        TryDeleteFile(sourcePath);
                        _logger.LogInformation("AudioFormatConverter: FLAC encoded via ffmpeg.");
                        return new AudioCaptureResult { Success = true, AudioFilePaths = [targetPath], Duration = duration };
                    }
                    var wavFallback = Path.ChangeExtension(targetPath, ".wav");
                    File.Move(sourcePath, wavFallback, overwrite: true);
                    _logger.LogWarning("AudioFormatConverter: ffmpeg unavailable; saved as WAV.");
                    return new AudioCaptureResult
                    {
                        Success = true,
                        AudioFilePaths = [wavFallback],
                        Duration = duration,
                        ErrorMessage = "ffmpeg not found in PATH; recording saved as WAV."
                    };

                default:
                    var wavOut = Path.ChangeExtension(targetPath, ".wav");
                    File.Move(sourcePath, wavOut, overwrite: true);
                    return new AudioCaptureResult { Success = true, AudioFilePaths = [wavOut], Duration = duration };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AudioFormatConverter: encoding failed; returning source file.");
            return new AudioCaptureResult
            {
                Success = true,
                AudioFilePaths = [sourcePath],
                Duration = duration,
                ErrorMessage = $"Encoding failed: {ex.Message}"
            };
        }
    }

    private bool TryEncodeToMp3WithMediaFoundation(string wavPath, string mp3Path, int bitRate)
    {
        try
        {
            using var reader = new WaveFileReader(wavPath);
            var mediaType = MediaFoundationEncoder.SelectMediaType(Mp3SubType, reader.WaveFormat, bitRate);
            if (mediaType is null) return false;
            using var encoder = new MediaFoundationEncoder(mediaType);
            encoder.Encode(mp3Path, reader);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AudioFormatConverter: MediaFoundation MP3 failed; trying LAME.");
            return false;
        }
    }

    private static void EncodeToMp3WithLame(string wavPath, string mp3Path, int bitRate)
    {
        using var reader = new WaveFileReader(wavPath);
        using var writer = new NAudio.Lame.LameMP3FileWriter(mp3Path, reader.WaveFormat, bitRate / 1000);
        reader.CopyTo(writer);
    }

    private async Task<bool> TryEncodeToFlacAsync(string wavPath, string flacPath)
    {
        try
        {
            var (exitCode, _) = await _processRunner.RunAsync(
                "ffmpeg", $"-y -i \"{wavPath}\" -compression_level 8 \"{flacPath}\"");
            return exitCode == 0 && File.Exists(flacPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AudioFormatConverter: ffmpeg FLAC failed.");
            return false;
        }
    }

    private static void TryDeleteFile(string? path)
    {
        if (path is not null)
            try { File.Delete(path); } catch { /* best-effort */ }
    }
}
