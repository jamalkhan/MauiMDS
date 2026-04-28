using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace MauiMds.Transcription.Engines.WhisperCpp;

/// <summary>
/// Transcription adapter that shells out to the whisper-cli binary from whisper.cpp.
/// Output is captured via --output-json and parsed into timestamped segments.
/// </summary>
public sealed class WhisperCppTranscriptionEngine : ITranscriptionEngine
{
    private readonly string _binaryPath;
    private readonly string _modelPath;
    private readonly ILogger _logger;

    public string Name => "Whisper.cpp";

    public bool IsAvailable =>
        !string.IsNullOrWhiteSpace(_binaryPath) && File.Exists(_binaryPath) &&
        !string.IsNullOrWhiteSpace(_modelPath) && File.Exists(_modelPath);

    public WhisperCppTranscriptionEngine(
        string binaryPath,
        string modelPath,
        ILogger<WhisperCppTranscriptionEngine> logger)
    {
        _binaryPath = binaryPath;
        _modelPath = modelPath;
        _logger = logger;
    }

    // whisper-cli only accepts: flac, mp3, ogg, wav
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".flac", ".mp3", ".ogg", ".wav" };

    public async Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(
        string audioFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException(
                "Whisper.cpp is not configured. " +
                "Set the whisper-cli binary path and model path in Preferences > Transcription.");
        }

        var outputBase = Path.Combine(Path.GetTempPath(), $"mauimds_whisper_{Guid.NewGuid():N}");
        var outputJson = outputBase + ".json";
        string? tempConvertedPath = null;

        try
        {
            _logger.LogInformation("WhisperCpp: starting transcription on {File}.", audioFilePath);
            progress?.Report(0.05);

            // whisper-cli does not support M4A/AAC — convert to a supported format first.
            var inputPath = audioFilePath;
            if (!SupportedExtensions.Contains(Path.GetExtension(audioFilePath)))
            {
                // Prefer MP3 (via ffmpeg) — smaller than WAV. Fall back to WAV via afconvert.
                var ffmpeg = FindFfmpeg();
                if (ffmpeg is not null)
                {
                    tempConvertedPath = Path.Combine(Path.GetTempPath(), $"mauimds_mp3_{Guid.NewGuid():N}.mp3");
                    _logger.LogInformation("WhisperCpp: converting {Ext} → MP3 via ffmpeg.",
                        Path.GetExtension(audioFilePath));
                    await ConvertToMp3Async(audioFilePath, tempConvertedPath, ffmpeg, cancellationToken);
                }
                else
                {
                    tempConvertedPath = Path.Combine(Path.GetTempPath(), $"mauimds_wav_{Guid.NewGuid():N}.wav");
                    _logger.LogInformation("WhisperCpp: ffmpeg not found — converting {Ext} → WAV via afconvert.",
                        Path.GetExtension(audioFilePath));
                    await ConvertToWavAsync(audioFilePath, tempConvertedPath, cancellationToken);
                }
                inputPath = tempConvertedPath;
                progress?.Report(0.20);
            }

            await RunWhisperAsync(inputPath, outputBase, cancellationToken);

            progress?.Report(0.85);

            if (!File.Exists(outputJson))
            {
                throw new InvalidOperationException(
                    $"whisper-cli completed but produced no JSON output. Expected: {outputJson}");
            }

            var segments = ParseWhisperJson(outputJson);
            progress?.Report(1.0);
            _logger.LogInformation("WhisperCpp: transcription complete — {Count} segments.", segments.Count);
            return segments;
        }
        finally
        {
            if (File.Exists(outputJson))
                try { File.Delete(outputJson); } catch { /* best-effort cleanup */ }
            if (tempConvertedPath is not null && File.Exists(tempConvertedPath))
                try { File.Delete(tempConvertedPath); } catch { /* best-effort cleanup */ }
        }
    }

    private static string? FindFfmpeg()
    {
        string[] candidates = ["/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg", "/usr/bin/ffmpeg"];
        return candidates.FirstOrDefault(File.Exists);
    }

    private async Task ConvertToMp3Async(
        string inputPath, string outputPath, string ffmpegPath, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = $"-i \"{inputPath}\" -q:a 2 -ac 1 -ar 16000 -y \"{outputPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            _logger.LogError("ffmpeg MP3 conversion failed (code {Code}): {Err}", process.ExitCode, stderr);
            throw new InvalidOperationException(
                $"Failed to convert audio to MP3 (ffmpeg exited {process.ExitCode}). {stderr}".Trim());
        }
    }

    private async Task ConvertToWavAsync(string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        // afconvert is always present on macOS.
        // LEI16@16000 = 16-bit PCM at 16 kHz mono — optimal for whisper.cpp.
        var psi = new ProcessStartInfo
        {
            FileName = "/usr/bin/afconvert",
            Arguments = $"-f WAVE -d LEI16@16000 -c 1 \"{inputPath}\" \"{outputPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            _logger.LogError("afconvert failed (code {Code}): {Err}", process.ExitCode, stderr);
            throw new InvalidOperationException(
                $"Failed to convert audio to WAV (afconvert exited {process.ExitCode}). {stderr}".Trim());
        }
    }

    private async Task RunWhisperAsync(
        string audioFilePath,
        string outputBase,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _binaryPath,
            // -m model  -f input  -oj output-json  -of output-base
            Arguments = $"-m \"{_modelPath}\" -f \"{audioFilePath}\" -oj -of \"{outputBase}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            _logger.LogError("whisper-cli exited with code {Code}. Stderr: {Err}",
                process.ExitCode, stderr);
            throw new InvalidOperationException(
                $"whisper-cli exited with code {process.ExitCode}. " +
                "Verify the binary and model paths are correct.");
        }
    }

    private IReadOnlyList<TranscriptSegment> ParseWhisperJson(string jsonPath)
    {
        var json = File.ReadAllText(jsonPath);

        WhisperOutput? doc;
        try
        {
            doc = JsonSerializer.Deserialize<WhisperOutput>(json);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse whisper-cli JSON output.");
            throw new InvalidOperationException("whisper-cli produced malformed JSON output.", ex);
        }

        if (doc?.Transcription is null)
        {
            return [];
        }

        var result = new List<TranscriptSegment>(doc.Transcription.Count);
        foreach (var entry in doc.Transcription)
        {
            var text = entry.Text.Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;

            result.Add(new TranscriptSegment
            {
                Text = text,
                Start = TimeSpan.FromMilliseconds(entry.Offsets.From),
                End = TimeSpan.FromMilliseconds(entry.Offsets.To)
            });
        }

        return result;
    }

    // whisper-cli JSON schema
    private sealed class WhisperOutput
    {
        [JsonPropertyName("transcription")]
        public List<WhisperEntry>? Transcription { get; set; }
    }

    private sealed class WhisperEntry
    {
        [JsonPropertyName("offsets")]
        public WhisperOffsets Offsets { get; set; } = new();

        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    private sealed class WhisperOffsets
    {
        [JsonPropertyName("from")]
        public long From { get; set; }

        [JsonPropertyName("to")]
        public long To { get; set; }
    }
}
