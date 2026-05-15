#if MACCATALYST
using AudioToolbox;
using AudioUnit;
using Foundation;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using Whisper.net;

namespace Rizedown.Transcription.Engines.WhisperNet;

/// <summary>
/// In-process transcription engine backed by Whisper.net (whisper.cpp via P/Invoke).
/// Uses CoreML for hardware acceleration on Apple Silicon and Intel Macs.
/// No subprocess — fully compatible with the Mac App Store sandbox.
/// Shares the same GGML model file format as the Whisper.cpp engine.
/// </summary>
public sealed class WhisperNetTranscriptionEngine : ITranscriptionEngine
{
    private readonly string _modelPath;
    private readonly ILogger _logger;

    public string Name => "Whisper.net";

    public bool IsAvailable =>
        !string.IsNullOrWhiteSpace(_modelPath) && File.Exists(_modelPath);

    public WhisperNetTranscriptionEngine(string modelPath, ILogger<WhisperNetTranscriptionEngine> logger)
    {
        _modelPath = modelPath;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(
        string audioFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            throw new InvalidOperationException(
                "Whisper.net model is not configured. " +
                "Set the GGML model path in Preferences → Transcription.");

        _logger.LogInformation("WhisperNet: starting transcription on {File}.", audioFilePath);
        progress?.Report(0.05);

        var inputPath = audioFilePath;
        string? tempWavPath = null;

        if (!string.Equals(Path.GetExtension(audioFilePath), ".wav", StringComparison.OrdinalIgnoreCase))
        {
            tempWavPath = Path.Combine(Path.GetTempPath(), $"rizedown_wn_{Guid.NewGuid():N}.wav");
            _logger.LogInformation("WhisperNet: converting {Ext} → WAV.", Path.GetExtension(audioFilePath));
            await ConvertToWavAsync(audioFilePath, tempWavPath);
            inputPath = tempWavPath;
            progress?.Report(0.15);
        }

        try
        {
            _logger.LogInformation("WhisperNet: loading model from {Path}.", _modelPath);
            using var factory   = WhisperFactory.FromPath(_modelPath);
            using var processor = factory.CreateBuilder()
                .WithLanguage("auto")
                .Build();

            var segments = new List<TranscriptSegment>();
            await using var stream = File.OpenRead(inputPath);
            progress?.Report(0.20);

            await foreach (var seg in processor.ProcessAsync(stream, cancellationToken))
            {
                var text = seg.Text.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    segments.Add(new TranscriptSegment
                    {
                        Text  = text,
                        Start = seg.Start,
                        End   = seg.End,
                    });
                }
            }

            progress?.Report(1.0);
            _logger.LogInformation("WhisperNet: transcription complete — {Count} segments.", segments.Count);
            return segments;
        }
        finally
        {
            if (tempWavPath is not null)
                try { File.Delete(tempWavPath); } catch { }
        }
    }

    // Native M4A→WAV via ExtAudioFile — no subprocess, App Store safe.
    // Decodes to 16 kHz mono 16-bit PCM WAV, the format whisper.cpp prefers.
    private static Task ConvertToWavAsync(string inputPath, string outputPath)
        => Task.Run(() =>
        {
            using var src = ExtAudioFile.OpenUrl(NSUrl.FromFilename(inputPath), out var openErr);
            if (src is null || openErr != ExtAudioFileError.OK)
                throw new InvalidOperationException($"Cannot open source audio ({openErr}).");

            var pcm16k = new AudioStreamBasicDescription
            {
                SampleRate       = 16000,
                Format           = AudioFormatType.LinearPCM,
                FormatFlags      = AudioFormatFlags.IsSignedInteger | AudioFormatFlags.IsPacked,
                FramesPerPacket  = 1,
                ChannelsPerFrame = 1,
                BitsPerChannel   = 16,
                BytesPerFrame    = 2,
                BytesPerPacket   = 2,
            };
            src.ClientDataFormat = pcm16k;

            using var dst = ExtAudioFile.CreateWithUrl(
                NSUrl.FromFilename(outputPath), AudioFileType.WAVE, pcm16k, AudioFileFlags.EraseFile, out var createErr);
            if (dst is null || createErr != ExtAudioFileError.OK)
                throw new InvalidOperationException($"Cannot create WAV output ({createErr}).");
            dst.ClientDataFormat = pcm16k;

            const int kFrames = 8192;
            const int bufSize  = kFrames * 2;
            var bufPtr = Marshal.AllocHGlobal(bufSize);
            try
            {
                using var abList = new AudioBuffers(1);
                while (true)
                {
                    abList[0] = new AudioBuffer
                    {
                        NumberChannels = 1,
                        DataByteSize   = bufSize,
                        Data           = bufPtr,
                    };
                    var framesRead = src.Read((uint)kFrames, abList, out var readErr);
                    if (framesRead == 0) break;
                    if (readErr != ExtAudioFileError.OK) break;
                    var writeErr = dst.Write(framesRead, abList);
                    if (writeErr != ExtAudioFileError.OK)
                        throw new InvalidOperationException($"WAV write error ({writeErr}).");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(bufPtr);
            }

            if (!File.Exists(outputPath))
                throw new InvalidOperationException("WAV output file was not created.");
        });
}
#endif
