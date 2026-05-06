using AVFoundation;
using AudioToolbox;
using CoreMedia;
using Foundation;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace MauiMds.AudioCapture.MacCatalyst;

/// <summary>
/// Runs alongside an active recording and periodically emits short WAV audio chunks
/// suitable for live transcription.
///
/// Each chunk is written as an intermediate M4A (via AVAssetWriter), then converted to
/// 16 kHz mono WAV via afconvert (the format whisper-cli and Apple Speech prefer).
/// The WAV file path is delivered via <see cref="ChunkReady"/> — the consumer must delete
/// the file when it is done with it.
/// </summary>
internal sealed class LiveAudioChunkWriter : IDisposable
{
    private readonly AudioCaptureOptions _options;
    private readonly ILogger _logger;
    private readonly TimeSpan _chunkInterval;

    private AVAssetWriter? _writer;
    private AVAssetWriterInput? _input;
    private string? _currentM4aPath;
    private TimeSpan _currentChunkStart;
    private CMTime _firstBufferTime = CMTime.Invalid;
    private bool _sessionStarted;
    private bool _disposed;

    private readonly object _lock = new();

    public event EventHandler<LiveAudioChunk>? ChunkReady;

    // Accumulated time in the current chunk.
    private TimeSpan _accumulatedDuration;

    public LiveAudioChunkWriter(AudioCaptureOptions options, ILogger logger)
    {
        _options = options;
        _logger = logger;
        _chunkInterval = options.LiveChunkInterval;
        StartNewChunk(TimeSpan.Zero);
    }

    // ── Buffer ingestion ──────────────────────────────────────────────────────

    public void AppendBuffer(CMSampleBuffer buffer)
    {
        lock (_lock)
        {
            if (_disposed || _writer is null || _input is null) return;

            if (!_sessionStarted)
            {
                _writer.StartWriting();
                _writer.StartSessionAtSourceTime(buffer.PresentationTimeStamp);
                _firstBufferTime = buffer.PresentationTimeStamp;
                _sessionStarted = true;
            }

            if (_input.ReadyForMoreMediaData)
                _input.AppendSampleBuffer(buffer);

            // Estimate elapsed time in this chunk from presentation timestamps.
            // _sessionStarted guarantees _firstBufferTime has been set to a real timestamp.
            if (_sessionStarted)
            {
                var elapsed = CMTime.Subtract(buffer.PresentationTimeStamp, _firstBufferTime);
                _accumulatedDuration = TimeSpan.FromSeconds(elapsed.Seconds);

                if (_accumulatedDuration >= _chunkInterval)
                    _ = FinalizeCurrentChunkAsync(isLast: false);
            }
        }
    }

    // ── Chunk finalization ────────────────────────────────────────────────────

    private async Task FinalizeCurrentChunkAsync(bool isLast)
    {
        AVAssetWriter? writer;
        AVAssetWriterInput? input;
        string? m4aPath;
        TimeSpan chunkStart;

        lock (_lock)
        {
            if (_writer is null || !_sessionStarted) return;

            writer   = _writer;
            input    = _input;
            m4aPath  = _currentM4aPath;
            chunkStart = _currentChunkStart;

            // Start a new chunk immediately so incoming buffers go to the new writer.
            if (!isLast)
                StartNewChunk(_currentChunkStart + _accumulatedDuration);
            else
            {
                _writer = null;
                _input  = null;
            }
        }

        if (m4aPath is null) return;

        try
        {
            input!.MarkAsFinished();
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            writer!.FinishWriting(() => tcs.TrySetResult());
            await tcs.Task;

            if (writer.Status == AVAssetWriterStatus.Failed)
            {
                _logger.LogWarning("LiveAudioChunkWriter: AVAssetWriter failed for chunk at {Start}: {Err}",
                    chunkStart, writer.Error?.LocalizedDescription);
                writer.Dispose();
                return;
            }
            writer.Dispose();

            // Convert to 16 kHz mono WAV for whisper-cli and Apple Speech chunk fallback.
            var wavPath = Path.ChangeExtension(m4aPath, ".wav");
            var converted = await ConvertToWavAsync(m4aPath, wavPath);
            TryDelete(m4aPath);

            if (!converted) return;

            _logger.LogDebug("LiveAudioChunkWriter: chunk ready at offset {Offset} ({Duration:g})",
                chunkStart, _accumulatedDuration);

            ChunkReady?.Invoke(this, new LiveAudioChunk(
                WavFilePath: wavPath,
                StartOffset: chunkStart,
                IsLastChunk: isLast,
                Source: AudioCaptureSource.Microphone));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LiveAudioChunkWriter: failed to finalize chunk at {Start}", chunkStart);
        }
    }

    public Task FlushAsync()
    {
        lock (_lock)
        {
            if (_writer is null || !_sessionStarted) return Task.CompletedTask;
        }
        return FinalizeCurrentChunkAsync(isLast: true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void StartNewChunk(TimeSpan startOffset)
    {
        _currentChunkStart = startOffset;
        _accumulatedDuration = TimeSpan.Zero;
        _sessionStarted = false;
        _firstBufferTime = CMTime.Invalid;

        _currentM4aPath = Path.Combine(
            Path.GetTempPath(),
            $"mauimds_chunk_{Guid.NewGuid():N}.m4a");

        _writer = new AVAssetWriter(NSUrl.FromFilename(_currentM4aPath), "com.apple.m4a-audio", out NSError? err);
        if (err is not null)
        {
            _logger.LogWarning("LiveAudioChunkWriter: could not create AVAssetWriter: {Err}", err.LocalizedDescription);
            _writer = null;
            return;
        }

        var settings = new AudioSettings
        {
            Format = AudioFormatType.MPEG4AAC,
            SampleRate = _options.SampleRate,
            NumberChannels = _options.ChannelCount,
            EncoderBitRate = _options.EncoderBitRate
        };
        _input = new AVAssetWriterInput("soun", settings) { ExpectsMediaDataInRealTime = true };
        _writer.AddInput(_input);
    }

    private static async Task<bool> ConvertToWavAsync(string m4aPath, string wavPath)
    {
        // 16 kHz mono signed 16-bit little-endian WAV — ideal for whisper-cli and Apple Speech.
        var psi = new ProcessStartInfo
        {
            FileName = "/usr/bin/afconvert",
            Arguments = $"-f WAVE -d LEI16@16000 -c 1 \"{m4aPath}\" \"{wavPath}\"",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = new Process { StartInfo = psi };
        proc.Start();
        await proc.WaitForExitAsync();
        return proc.ExitCode == 0 && File.Exists(wavPath);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _input?.Dispose();
            _writer?.Dispose();
            _writer = null;
            _input = null;
        }
    }
}
