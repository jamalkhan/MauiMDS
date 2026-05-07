#pragma warning disable CA1416  // NAudio resampling APIs require Windows; this file is Windows-only.
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MauiMds.AudioCapture.Windows;

/// <summary>
/// Accumulates raw PCM bytes from the microphone during recording and periodically emits
/// short 16 kHz mono WAV chunks suitable for live transcription.
///
/// Mirrors the behaviour of <c>LiveAudioChunkWriter</c> on Mac Catalyst, but uses NAudio
/// in-process resampling instead of AVAssetWriter + afconvert.
/// </summary>
internal sealed class WindowsLiveChunkWriter : IDisposable
{
    private readonly WaveFormat _inputFormat;
    private readonly long _bytesPerChunk;
    private readonly ILogger _logger;

    private readonly object _lock = new();
    private MemoryStream? _currentStream;
    private TimeSpan _currentChunkStart;
    private long _bytesInCurrentChunk;
    private bool _finalizing;
    private bool _disposed;

    public event EventHandler<LiveAudioChunk>? ChunkReady;

    public WindowsLiveChunkWriter(WaveFormat inputFormat, TimeSpan chunkInterval, ILogger logger)
    {
        _inputFormat = inputFormat;
        _bytesPerChunk = (long)(inputFormat.AverageBytesPerSecond * chunkInterval.TotalSeconds);
        _logger = logger;
        StartNewChunk(TimeSpan.Zero);
    }

    // Called from WaveInEvent.DataAvailable on a background thread.
    public void Write(byte[] buffer, int offset, int count)
    {
        bool shouldFinalize;
        lock (_lock)
        {
            if (_disposed || _currentStream is null) return;
            _currentStream.Write(buffer, offset, count);
            _bytesInCurrentChunk += count;
            shouldFinalize = !_finalizing && _bytesInCurrentChunk >= _bytesPerChunk;
            if (shouldFinalize) _finalizing = true;
        }

        if (shouldFinalize)
            _ = FinalizeChunkAsync(isLast: false);
    }

    /// <summary>Finalizes any remaining audio as the last chunk. Awaited during recording stop.</summary>
    public Task FlushLastChunkAsync()
    {
        lock (_lock) { _finalizing = true; }
        return FinalizeChunkAsync(isLast: true);
    }

    private async Task FinalizeChunkAsync(bool isLast)
    {
        MemoryStream stream;
        TimeSpan chunkStart;

        lock (_lock)
        {
            if (_currentStream is null)
            {
                _finalizing = false;
                return;
            }

            stream = _currentStream;
            chunkStart = _currentChunkStart;

            if (!isLast)
            {
                var chunkDuration = TimeSpan.FromSeconds((double)_bytesInCurrentChunk / _inputFormat.AverageBytesPerSecond);
                StartNewChunk(_currentChunkStart + chunkDuration);
            }
            else
            {
                _currentStream = null;
            }

            // Reset before releasing the lock so concurrent Write() calls
            // can schedule the next finalization without delay.
            _finalizing = false;
        }

        if (stream.Length == 0) { stream.Dispose(); return; }

        var outputPath = Path.Combine(Path.GetTempPath(), $"mauimds_chunk_{Guid.NewGuid():N}.wav");
        try
        {
            stream.Position = 0;
            var wavPath = await Task.Run(() => ResampleToWhisperWav(stream, outputPath));
            stream.Dispose();

            if (wavPath is null) return;

            _logger.LogDebug("WindowsLiveChunkWriter: chunk ready at offset {Offset}", chunkStart);
            ChunkReady?.Invoke(this, new LiveAudioChunk(
                WavFilePath: wavPath,
                StartOffset: chunkStart,
                IsLastChunk: isLast,
                Source: AudioCaptureSource.Microphone));
        }
        catch (Exception ex)
        {
            stream.Dispose();
            TryDelete(outputPath);
            _logger.LogWarning(ex, "WindowsLiveChunkWriter: failed to finalize chunk at {Start}", chunkStart);
        }
    }

    // Converts the in-memory raw PCM to a 16 kHz mono 16-bit WAV that whisper-cli expects.
    private string? ResampleToWhisperWav(MemoryStream rawPcm, string outputPath)
    {
        try
        {
            using var rawStream = new RawSourceWaveStream(rawPcm, _inputFormat);
            ISampleProvider sample = rawStream.ToSampleProvider();
            if (_inputFormat.Channels > 1)
                sample = new StereoToMonoSampleProvider(sample);

            var resampled = new WdlResamplingSampleProvider(sample, 16000);
            WaveFileWriter.CreateWaveFile16(outputPath, resampled);
            return File.Exists(outputPath) ? outputPath : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WindowsLiveChunkWriter: resampling to 16 kHz mono failed");
            TryDelete(outputPath);
            return null;
        }
    }

    private void StartNewChunk(TimeSpan startOffset)
    {
        _currentChunkStart = startOffset;
        _bytesInCurrentChunk = 0;
        _currentStream = new MemoryStream();
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _currentStream?.Dispose();
            _currentStream = null;
        }
    }
}
