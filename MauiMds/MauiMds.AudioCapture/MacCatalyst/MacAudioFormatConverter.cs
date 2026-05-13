using AudioToolbox;
using AudioUnit;
using AVFoundation;
using Foundation;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

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

    // ── FLAC: native ExtAudioFile — no subprocess, App Store safe ──────────────

    private Task<AudioCaptureResult> ConvertToFlacAsync(
        string sourcePath, string targetPath, TimeSpan duration)
        => Task.Run(() => DoConvertToFlac(sourcePath, targetPath, duration));

    private AudioCaptureResult DoConvertToFlac(
        string sourcePath, string targetPath, TimeSpan duration)
    {
        var srcUrl = NSUrl.FromFilename(sourcePath);
        var dstUrl = NSUrl.FromFilename(targetPath);

        using var src = ExtAudioFile.OpenUrl(srcUrl, out var openErr);
        if (src is null || openErr != ExtAudioFileError.OK)
        {
            _logger.LogError("FLAC: ExtAudioFile.OpenUrl failed ({Err})", openErr);
            return Fail($"FLAC conversion failed: cannot open source file ({openErr}).");
        }

        var fileFmt = src.FileDataFormat;
        var channels = fileFmt.ChannelsPerFrame;

        // Client format: interleaved signed 16-bit PCM — used on both ends so
        // ExtAudioFile handles codec decode (source) and FLAC encode (destination).
        var pcm = new AudioStreamBasicDescription
        {
            SampleRate       = fileFmt.SampleRate,
            Format           = AudioFormatType.LinearPCM,
            FormatFlags      = AudioFormatFlags.IsSignedInteger | AudioFormatFlags.IsPacked,
            FramesPerPacket  = 1,
            ChannelsPerFrame = channels,
            BitsPerChannel   = 16,
            BytesPerFrame    = 2 * channels,
            BytesPerPacket   = 2 * channels,
        };
        src.ClientDataFormat = pcm;

        var flacFmt = new AudioStreamBasicDescription
        {
            SampleRate       = fileFmt.SampleRate,
            Format           = AudioFormatType.Flac,
            ChannelsPerFrame = channels,
            BitsPerChannel   = 16,
        };

        using var dst = ExtAudioFile.CreateWithUrl(
            dstUrl, AudioFileType.FLAC, flacFmt, AudioFileFlags.EraseFile, out var createErr);
        if (dst is null || createErr != ExtAudioFileError.OK)
        {
            _logger.LogError("FLAC: ExtAudioFile.CreateWithUrl failed ({Err})", createErr);
            return Fail($"FLAC conversion failed: cannot create output file ({createErr}).");
        }
        dst.ClientDataFormat = pcm;

        const int kFrames = 8192;
        var bufSize = kFrames * (int)pcm.BytesPerFrame;
        var bufPtr  = Marshal.AllocHGlobal(bufSize);
        try
        {
            using var abList = new AudioBuffers(1);
            while (true)
            {
                abList[0] = new AudioBuffer
                {
                    NumberChannels = channels,
                    DataByteSize   = bufSize,
                    Data           = bufPtr,
                };

                var framesRead = src.Read((uint)kFrames, abList, out var readErr);
                if (framesRead == 0) break;

                if (readErr != ExtAudioFileError.OK)
                {
                    _logger.LogWarning("FLAC: read stopped with {Err} after {Frames} frames", readErr, framesRead);
                    break;
                }

                var writeErr = dst.Write(framesRead, abList);
                if (writeErr != ExtAudioFileError.OK)
                {
                    _logger.LogError("FLAC: write error {Err}", writeErr);
                    return Fail($"FLAC conversion failed: write error ({writeErr}).");
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(bufPtr);
        }

        if (!File.Exists(targetPath))
            return Fail("FLAC conversion failed: output file was not created.");

        return new AudioCaptureResult { Success = true, AudioFilePaths = [targetPath], Duration = duration };
    }

    // ── MP3: ffmpeg when available; native AAC/M4A fallback (App Store safe) ───

    private async Task<AudioCaptureResult> ConvertToMp3Async(
        string sourcePath, string targetPath, TimeSpan duration)
    {
        var ffmpeg = FindFfmpeg();
        if (ffmpeg is not null)
        {
            var (exitCode, stderr) = await _processRunner.RunAsync(
                ffmpeg, $"-i \"{sourcePath}\" -q:a 2 -y \"{targetPath}\"");

            if (exitCode != 0 || !File.Exists(targetPath))
            {
                _logger.LogError("ffmpeg MP3 failed (code {Code}): {Err}", exitCode, stderr);
                return Fail($"MP3 conversion failed (ffmpeg exited {exitCode}). {stderr}".Trim());
            }
            if (!string.IsNullOrWhiteSpace(stderr))
                _logger.LogDebug("ffmpeg MP3 stderr: {Stderr}", stderr);
            return new AudioCaptureResult { Success = true, AudioFilePaths = [targetPath], Duration = duration };
        }

        // ffmpeg not found (e.g. sandboxed App Store build where subprocess execution
        // is restricted). Fall back to native AAC/M4A via AVFoundation — macOS does not
        // provide a native MP3 encoder, so M4A is the App Store-safe equivalent.
        _logger.LogInformation(
            "ffmpeg not found — falling back to native AAC export (M4A) instead of MP3.");
        var m4aPath = Path.ChangeExtension(targetPath, ".m4a");
        return await ExportToM4aAsync(sourcePath, m4aPath, duration);
    }

    private async Task<AudioCaptureResult> ExportToM4aAsync(
        string sourcePath, string targetPath, TimeSpan duration)
    {
        var asset = AVUrlAsset.FromUrl(NSUrl.FromFilename(sourcePath));
        using var session = new AVAssetExportSession(asset, AVAssetExportSessionPreset.AppleM4A)
        {
            OutputUrl      = NSUrl.FromFilename(targetPath),
            OutputFileType = "com.apple.m4a-audio",
        };

        await session.ExportTaskAsync();

        if (session.Status != AVAssetExportSessionStatus.Completed || !File.Exists(targetPath))
        {
            var errMsg = session.Error?.LocalizedDescription ?? "Unknown export error";
            _logger.LogError("AVAssetExportSession failed: {Err}", errMsg);
            return Fail($"Audio export failed: {errMsg}");
        }

        return new AudioCaptureResult { Success = true, AudioFilePaths = [targetPath], Duration = duration };
    }

    private static string? FindFfmpeg()
    {
        string[] candidates = ["/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg", "/usr/bin/ffmpeg"];
        return candidates.FirstOrDefault(File.Exists);
    }

    private static AudioCaptureResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
