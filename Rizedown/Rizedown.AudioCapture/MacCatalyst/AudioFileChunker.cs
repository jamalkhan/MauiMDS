using AudioToolbox;
using AudioUnit;
using AVFoundation;
using CoreMedia;
using Foundation;
using System.Runtime.InteropServices;

namespace Rizedown.AudioCapture.MacCatalyst;

/// <summary>
/// Splits an audio file into fixed-duration 16 kHz mono WAV chunks for sequential processing.
/// Each chunk is written to a temp path; the caller's delegate should delete it when done.
/// </summary>
public static class AudioFileChunker
{
    private const int SampleRate  = 16000;
    private const int BytesPerFrame = 2; // 16-bit mono

    private static readonly AudioStreamBasicDescription Pcm16kDesc = new()
    {
        SampleRate       = SampleRate,
        Format           = AudioFormatType.LinearPCM,
        FormatFlags      = AudioFormatFlags.IsSignedInteger | AudioFormatFlags.IsPacked,
        FramesPerPacket  = 1,
        ChannelsPerFrame = 1,
        BitsPerChannel   = 16,
        BytesPerFrame    = BytesPerFrame,
        BytesPerPacket   = BytesPerFrame,
    };

    public static double GetDurationSeconds(string audioFilePath)
    {
        try
        {
            var asset = AVAsset.FromUrl(NSUrl.FromFilename(audioFilePath));
            return asset.Duration.Seconds;
        }
        catch { return 0; }
    }

    /// <summary>
    /// Reads audioFilePath in sequential chunks of chunkSeconds, writing each chunk as a temp
    /// WAV file and invoking onChunk. Deletes each temp WAV after onChunk completes.
    /// Progress is reported in [0, 1] as chunks are processed.
    /// </summary>
    public static async Task ProcessChunksAsync(
        string audioFilePath,
        int chunkSeconds,
        Func<string, TimeSpan, CancellationToken, Task> onChunk,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        const int ioBufferFrames = 8192;
        const int ioBufferBytes  = ioBufferFrames * BytesPerFrame;

        var framesPerChunk = SampleRate * chunkSeconds;
        var totalSeconds   = GetDurationSeconds(audioFilePath);

        using var src = ExtAudioFile.OpenUrl(NSUrl.FromFilename(audioFilePath), out var openErr);
        if (src is null || openErr != ExtAudioFileError.OK)
            throw new InvalidOperationException($"AudioFileChunker: cannot open {audioFilePath}");
        src.ClientDataFormat = Pcm16kDesc;

        var bufPtr = Marshal.AllocHGlobal(ioBufferBytes);
        try
        {
            long processedFrames = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var chunkStart = TimeSpan.FromSeconds((double)processedFrames / SampleRate);
                var wavPath    = Path.Combine(Path.GetTempPath(), $"rizedown_chunk_{Guid.NewGuid():N}.wav");

                var framesWritten = await Task.Run(
                    () => WriteWavChunk(src, wavPath, framesPerChunk, ioBufferFrames, ioBufferBytes, bufPtr), ct);

                if (framesWritten == 0) break;

                processedFrames += framesWritten;

                if (totalSeconds > 0)
                    progress?.Report(Math.Min(0.98, (double)processedFrames / SampleRate / totalSeconds));

                try
                {
                    await onChunk(wavPath, chunkStart, ct);
                }
                finally
                {
                    try { if (File.Exists(wavPath)) File.Delete(wavPath); } catch { }
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(bufPtr);
        }

        progress?.Report(1.0);
    }

    private static long WriteWavChunk(
        ExtAudioFile src,
        string wavPath,
        int maxFrames,
        int ioBufferFrames,
        int ioBufferBytes,
        IntPtr bufPtr)
    {
        using var dst = ExtAudioFile.CreateWithUrl(
            NSUrl.FromFilename(wavPath), AudioFileType.WAVE, Pcm16kDesc, AudioFileFlags.EraseFile, out var createErr);
        if (dst is null || createErr != ExtAudioFileError.OK) return 0;
        dst.ClientDataFormat = Pcm16kDesc;

        using var abList  = new AudioBuffers(1);
        long totalWritten = 0;

        while (totalWritten < maxFrames)
        {
            var toRead = (uint)Math.Min(ioBufferFrames, maxFrames - (int)totalWritten);
            abList[0] = new AudioBuffer
            {
                NumberChannels = 1,
                DataByteSize   = ioBufferBytes,
                Data           = bufPtr,
            };
            var framesRead = src.Read(toRead, abList, out var readErr);
            if (framesRead == 0) break;
            if (readErr != ExtAudioFileError.OK) break;
            if (dst.Write(framesRead, abList) != ExtAudioFileError.OK) break;
            totalWritten += framesRead;
        }

        return totalWritten;
    }
}
