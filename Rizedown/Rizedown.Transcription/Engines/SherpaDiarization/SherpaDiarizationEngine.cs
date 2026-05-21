using Microsoft.Extensions.Logging;
using SherpaOnnx;
#if MACCATALYST
using AudioToolbox;
using AudioUnit;
using Foundation;
using System.Runtime.InteropServices;
#endif

namespace Rizedown.Transcription.Engines.SherpaDiarization;

/// <summary>
/// Speaker diarization adapter backed by sherpa-onnx (k2-fsa).
/// Runs entirely in-process via P/Invoke — no subprocess, no JIT — making it
/// compatible with the Mac App Store sandbox.
///
/// Requires two ONNX model files downloaded from the sherpa-onnx model repository:
///   - Segmentation: sherpa-onnx-pyannote-segmentation-3-0/model.onnx (~17 MB)
///   - Embedding:    e.g. wespeaker-voxceleb-resnet34-LM.onnx (~26 MB)
///
/// Note: the NuGet package ships an osx-arm64 dylib; there is no maccatalyst-specific
/// native package. The dylib loads under Mac Catalyst on Apple Silicon in practice,
/// but this is untested by the sherpa-onnx maintainers.
/// </summary>
public sealed class SherpaDiarizationEngine : IDiarizationEngine
{
    private readonly string _segmentationModelPath;
    private readonly string _embeddingModelPath;
    private readonly ILogger _logger;

    public string Name => "Sherpa-ONNX";

    public bool IsAvailable =>
        !string.IsNullOrWhiteSpace(_segmentationModelPath) && File.Exists(_segmentationModelPath) &&
        !string.IsNullOrWhiteSpace(_embeddingModelPath)    && File.Exists(_embeddingModelPath);

    public SherpaDiarizationEngine(
        string segmentationModelPath,
        string embeddingModelPath,
        ILogger<SherpaDiarizationEngine> logger)
    {
        _segmentationModelPath = segmentationModelPath;
        _embeddingModelPath    = embeddingModelPath;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SpeakerSegment>> DiarizeAsync(
        string audioFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            var missing = new List<string>(2);
            if (string.IsNullOrWhiteSpace(_segmentationModelPath) || !File.Exists(_segmentationModelPath))
                missing.Add("segmentation model");
            if (string.IsNullOrWhiteSpace(_embeddingModelPath) || !File.Exists(_embeddingModelPath))
                missing.Add("embedding model");
            throw new InvalidOperationException(
                $"Sherpa-ONNX model file(s) not found: {string.Join(", ", missing)}. " +
                "Run Scripts/download-sherpa-models.sh then rebuild to bundle them.");
        }

        _logger.LogInformation("Sherpa: starting diarization on {File}.", audioFilePath);
        progress?.Report(0.02);

        var config = new OfflineSpeakerDiarizationConfig();
        config.Segmentation.Pyannote.Model = _segmentationModelPath;
        config.Embedding.Model = _embeddingModelPath;
        config.Clustering.NumClusters = -1;   // auto-detect speaker count
        config.Clustering.Threshold   = 0.5f;
        config.MinDurationOn  = 0.3f;
        config.MinDurationOff = 0.5f;

        // If the native dylib is missing, OfflineSpeakerDiarization's constructor throws
        // DllNotFoundException but still registers a finalizer. That finalizer also calls
        // the missing DLL and — because exceptions from finalizers are fatal — crashes the
        // process. Suppress the finalizer on the failed object before rethrowing.
        OfflineSpeakerDiarization? sd = null;
        try
        {
            sd = new OfflineSpeakerDiarization(config);
        }
        catch (DllNotFoundException ex)
        {
            if (sd is not null) GC.SuppressFinalize(sd);
            throw new InvalidOperationException(
                "Sherpa-ONNX native library (libsherpa-onnx-c-api.dylib) could not be loaded. " +
                "Rebuild the app to ensure the dylib is bundled.", ex);
        }

        using (sd)
        {
            _logger.LogInformation("Sherpa: model loaded. SampleRate={Rate}.", sd.SampleRate);

            var samples = await LoadAsFloatSamplesAsync(audioFilePath, sd.SampleRate, cancellationToken);
            _logger.LogInformation("Sherpa: loaded {Frames} frames.", samples.Length);

            progress?.Report(0.10);

            OfflineSpeakerDiarizationSegment[]? rawSegments = null;
            await Task.Run(() =>
            {
                OfflineSpeakerDiarizationProgressCallback cb = (processed, total, _) =>
                {
                    if (total > 0)
                        progress?.Report(0.10 + 0.88 * processed / total);
                    return 0;
                };
                rawSegments = sd.ProcessWithCallback(samples, cb, IntPtr.Zero);
            }, cancellationToken);

            var result = ConvertSegments(rawSegments ?? []);
            progress?.Report(1.0);
            _logger.LogInformation("Sherpa: diarization complete — {Count} speaker segments.", result.Count);
            return result;
        }
    }

    private static IReadOnlyList<SpeakerSegment> ConvertSegments(OfflineSpeakerDiarizationSegment[] raw)
    {
        var list = new List<SpeakerSegment>(raw.Length);
        foreach (var seg in raw)
        {
            list.Add(new SpeakerSegment
            {
                SpeakerLabel = $"SPEAKER_{seg.Speaker:D2}",
                Start = TimeSpan.FromSeconds(seg.Start),
                End   = TimeSpan.FromSeconds(seg.End),
            });
        }
        return list;
    }

#if MACCATALYST
    private static async Task<float[]> LoadAsFloatSamplesAsync(
        string audioFilePath, int sampleRate, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var floatFormat = new AudioStreamBasicDescription
            {
                SampleRate       = sampleRate,
                Format           = AudioFormatType.LinearPCM,
                FormatFlags      = AudioFormatFlags.IsFloat | AudioFormatFlags.IsPacked,
                FramesPerPacket  = 1,
                ChannelsPerFrame = 1,
                BitsPerChannel   = 32,
                BytesPerFrame    = 4,
                BytesPerPacket   = 4,
            };

            using var src = ExtAudioFile.OpenUrl(NSUrl.FromFilename(audioFilePath), out var openErr);
            if (src is null || openErr != ExtAudioFileError.OK)
                throw new InvalidOperationException(
                    $"Sherpa: cannot open audio file for diarization ({openErr}): {audioFilePath}");
            src.ClientDataFormat = floatFormat;

            const int kFrames = 8192;
            const int kBufBytes = kFrames * 4; // 4 bytes per float
            var tempBuf = new float[kFrames];
            var allSamples = new List<float>();
            var nativePtr = Marshal.AllocHGlobal(kBufBytes);
            try
            {
                using var abList = new AudioBuffers(1);
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    abList[0] = new AudioBuffer
                    {
                        NumberChannels = 1,
                        DataByteSize   = kBufBytes,
                        Data           = nativePtr,
                    };
                    var framesRead = src.Read(kFrames, abList, out var readErr);
                    if (framesRead == 0) break;
                    if (readErr != ExtAudioFileError.OK) break;
                    Marshal.Copy(nativePtr, tempBuf, 0, (int)framesRead);
                    allSamples.AddRange(new ReadOnlySpan<float>(tempBuf, 0, (int)framesRead));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(nativePtr);
            }
            return allSamples.ToArray();
        }, ct);
    }
#endif
}
