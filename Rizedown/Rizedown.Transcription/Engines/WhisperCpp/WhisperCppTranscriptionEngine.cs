using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
#if MACCATALYST
using AudioToolbox;
using AudioUnit;
using Foundation;
using System.Runtime.InteropServices;
#endif

namespace Rizedown.Transcription.Engines.WhisperCpp;

/// <summary>
/// Transcription adapter that shells out to the whisper-cli binary from whisper.cpp.
/// Output is captured via --output-json and parsed into timestamped segments.
/// </summary>
public sealed partial class WhisperCppTranscriptionEngine : ITranscriptionEngine
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

    public ILiveTranscriptionSession CreateLiveSession() => new WhisperCppLiveSession(this, _logger);

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

        var outputBase = Path.Combine(Path.GetTempPath(), $"rizedown_whisper_{Guid.NewGuid():N}");
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
#if MACCATALYST
                tempConvertedPath = Path.Combine(Path.GetTempPath(), $"rizedown_wav_{Guid.NewGuid():N}.wav");
                _logger.LogInformation("WhisperCpp: converting {Ext} → WAV (native ExtAudioFile).",
                    Path.GetExtension(audioFilePath));
                await ConvertToWavNativeAsync(audioFilePath, tempConvertedPath, cancellationToken);
#else
                // Prefer MP3 (via ffmpeg) — smaller than WAV. Fall back to WAV via afconvert.
                var ffmpeg = FindFfmpeg();
                if (ffmpeg is not null)
                {
                    tempConvertedPath = Path.Combine(Path.GetTempPath(), $"rizedown_mp3_{Guid.NewGuid():N}.mp3");
                    _logger.LogInformation("WhisperCpp: converting {Ext} → MP3 via ffmpeg.",
                        Path.GetExtension(audioFilePath));
                    await ConvertToMp3Async(audioFilePath, tempConvertedPath, ffmpeg, cancellationToken);
                }
                else
                {
                    tempConvertedPath = Path.Combine(Path.GetTempPath(), $"rizedown_wav_{Guid.NewGuid():N}.wav");
                    _logger.LogInformation("WhisperCpp: ffmpeg not found — converting {Ext} → WAV via afconvert.",
                        Path.GetExtension(audioFilePath));
                    await ConvertToWavAsync(audioFilePath, tempConvertedPath, cancellationToken);
                }
#endif
                inputPath = tempConvertedPath;
                progress?.Report(0.20);
            }

            await RunWhisperAsync(inputPath, outputBase, progress, cancellationToken);

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

#if MACCATALYST
    // Native M4A→WAV via ExtAudioFile — no subprocess, App Store safe.
    // Decodes to 16 kHz mono 16-bit PCM WAV, the format whisper-cli prefers.
    private static Task ConvertToWavNativeAsync(string inputPath, string outputPath, CancellationToken _)
        => Task.Run(() =>
        {
            using var src = ExtAudioFile.OpenUrl(NSUrl.FromFilename(inputPath), out var openErr);
            if (src is null || openErr != ExtAudioFileError.OK)
                throw new InvalidOperationException($"Cannot open source audio for WAV conversion ({openErr}).");

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
#else
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

        try
        {
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0 || !File.Exists(outputPath))
            {
                _logger.LogError("ffmpeg MP3 conversion failed (code {Code}): {Err}", process.ExitCode, stderr);
                throw new InvalidOperationException(
                    $"Failed to convert audio to MP3 (ffmpeg exited {process.ExitCode}). {stderr}".Trim());
            }
        }
        catch when (cancellationToken.IsCancellationRequested)
        {
            KillSafely(process);
            throw;
        }
    }

    private async Task ConvertToWavAsync(string inputPath, string outputPath, CancellationToken cancellationToken)
    {
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

        try
        {
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0 || !File.Exists(outputPath))
            {
                _logger.LogError("afconvert failed (code {Code}): {Err}", process.ExitCode, stderr);
                throw new InvalidOperationException(
                    $"Failed to convert audio to WAV (afconvert exited {process.ExitCode}). {stderr}".Trim());
            }
        }
        catch when (cancellationToken.IsCancellationRequested)
        {
            KillSafely(process);
            throw;
        }
    }
#endif

    // Matches: "whisper_print_progress_callback: progress = 25%"
    private static readonly Regex WhisperProgressRegex =
        new(@"progress\s*=\s*(\d+)\s*%", RegexOptions.Compiled);

    private async Task RunWhisperAsync(
        string audioFilePath,
        string outputBase,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _binaryPath,
            Arguments = $"-m \"{_modelPath}\" -f \"{audioFilePath}\" -oj -of \"{outputBase}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stderrCapture = new System.Text.StringBuilder();

        var stderrTask = Task.Run(async () =>
        {
            while (true)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken);
                if (line is null) break;
                stderrCapture.AppendLine(line);

                if (!string.IsNullOrWhiteSpace(line))
                    _logger.LogDebug("whisper-cli: {Line}", line);

                var m = WhisperProgressRegex.Match(line);
                if (m.Success && int.TryParse(m.Groups[1].Value, out var pct))
                {
                    // Map whisper's 0–100 into our 20%–85% band (20% was used for conversion).
                    progress?.Report(0.20 + pct / 100.0 * 0.65);
                }
            }
        }, cancellationToken);

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);

        try
        {
            await Task.WhenAll(stderrTask, stdoutTask);
            await process.WaitForExitAsync(cancellationToken);
        }
        catch when (cancellationToken.IsCancellationRequested)
        {
            KillSafely(process);
            throw;
        }

        if (process.ExitCode != 0)
        {
            _logger.LogError("whisper-cli exited with code {Code}.\nStderr:\n{Err}",
                process.ExitCode, stderrCapture.ToString());
            throw new InvalidOperationException(
                $"whisper-cli exited with code {process.ExitCode}. " +
                "Verify the binary and model paths are correct.");
        }
    }

    private static void KillSafely(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
    }

    private IReadOnlyList<TranscriptSegment> ParseWhisperJson(string jsonPath)
    {
        var json = File.ReadAllText(jsonPath);

        WhisperOutput? doc;
        try
        {
            doc = JsonSerializer.Deserialize(json, WhisperJsonContext.Default.WhisperOutput);
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

    [JsonSerializable(typeof(WhisperOutput))]
    private sealed partial class WhisperJsonContext : JsonSerializerContext { }
}
