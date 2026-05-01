using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace MauiMds.Transcription.Engines.Pyannote;

/// <summary>
/// Diarization adapter that shells out to a local Python environment running pyannote.audio 3.x.
/// An inline Python script is written to a temp file, executed, and its stdout parsed as
/// whitespace-delimited "start end speaker" lines.
///
/// Setup requirements:
///   pip install pyannote.audio
///   Accept model terms at https://huggingface.co/pyannote/speaker-diarization-3.1
///   Provide a Hugging Face access token in Preferences → Transcription.
/// </summary>
public sealed class PyannoteDiarizationEngine : IDiarizationEngine
{
    private readonly string _pythonPath;
    private readonly string _hfToken;
    private readonly ILogger _logger;

    private static readonly HashSet<string> WavCompatibleExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".wav", ".flac", ".mp3", ".ogg" };

    public string Name => "pyannote.audio";

    public bool IsAvailable =>
        !string.IsNullOrWhiteSpace(_pythonPath) && File.Exists(_pythonPath);

    public PyannoteDiarizationEngine(string pythonPath, string hfToken, ILogger<PyannoteDiarizationEngine> logger)
    {
        _pythonPath = pythonPath;
        _hfToken = hfToken;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SpeakerSegment>> DiarizeAsync(
        string audioFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            throw new InvalidOperationException(
                "pyannote.audio is not configured. " +
                "Set the Python executable path in Preferences → Transcription.");

        var scriptPath = Path.Combine(Path.GetTempPath(), $"mauimds_pyannote_{Guid.NewGuid():N}.py");
        string? tempWavPath = null;

        try
        {
            // pyannote uses torchaudio/soundfile which cannot read M4A/AAC.
            // Convert to WAV first when needed.
            var inputPath = audioFilePath;
            if (!WavCompatibleExtensions.Contains(Path.GetExtension(audioFilePath)))
            {
                tempWavPath = Path.Combine(Path.GetTempPath(), $"mauimds_pyannote_{Guid.NewGuid():N}.wav");
                _logger.LogInformation("Pyannote: converting {Ext} → WAV for diarization.",
                    Path.GetExtension(audioFilePath));
                await ConvertToWavAsync(audioFilePath, tempWavPath, cancellationToken);
                inputPath = tempWavPath;
            }

            await File.WriteAllTextAsync(scriptPath, PyannoteScript, cancellationToken);

            _logger.LogInformation("Pyannote: starting diarization on {File}.", inputPath);
            progress?.Report(0.05);

            var stdout = await RunPythonAsync(scriptPath, inputPath, cancellationToken);

            progress?.Report(0.95);

            var segments = ParseOutput(stdout);
            progress?.Report(1.0);
            _logger.LogInformation("Pyannote: diarization complete — {Count} speaker segments.", segments.Count);
            return segments;
        }
        finally
        {
            TryDelete(scriptPath);
            if (tempWavPath is not null) TryDelete(tempWavPath);
        }
    }

    private async Task ConvertToWavAsync(string inputPath, string outputPath, CancellationToken ct)
    {
        // Use afconvert (always present on macOS) to produce 16-bit PCM WAV at 16 kHz mono.
        var psi = new ProcessStartInfo
        {
            FileName = "/usr/bin/afconvert",
            Arguments = $"-f WAVE -d LEI16@16000 -c 1 \"{inputPath}\" \"{outputPath}\"",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"afconvert failed (exit {process.ExitCode}): {stderr}");
    }

    private async Task<string> RunPythonAsync(
        string scriptPath,
        string audioFilePath,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _pythonPath,
            Arguments = $"\"{scriptPath}\" \"{audioFilePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Pass HF token via environment variable so huggingface_hub picks it up automatically.
        if (!string.IsNullOrWhiteSpace(_hfToken))
            psi.Environment["HF_TOKEN"] = _hfToken;

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await Task.WhenAll(stdoutTask, stderrTask);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var stderr = await stderrTask;
            _logger.LogError("pyannote exited with code {Code}. Stderr: {Err}", process.ExitCode, stderr);
            throw new InvalidOperationException(
                $"pyannote.audio exited with code {process.ExitCode}. " +
                "Ensure pyannote.audio 3.x is installed and a Hugging Face token is set in Preferences → Transcription.");
        }

        return await stdoutTask;
    }

    private static IReadOnlyList<SpeakerSegment> ParseOutput(string output)
    {
        var result = new List<SpeakerSegment>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) continue;

            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var start))
                continue;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var end))
                continue;

            result.Add(new SpeakerSegment
            {
                Start = TimeSpan.FromSeconds(start),
                End = TimeSpan.FromSeconds(end),
                SpeakerLabel = parts[2]
            });
        }

        return result;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    // Printed to a temp .py file and executed by the user-configured Python interpreter.
    // HF_TOKEN is injected via the subprocess environment — no change needed here.
    private const string PyannoteScript = """
        import sys
        from pyannote.audio import Pipeline

        if len(sys.argv) < 2:
            sys.exit("Usage: pyannote_diarize.py <audio_file>")

        pipeline = Pipeline.from_pretrained("pyannote/speaker-diarization-3.1")
        diarization = pipeline(sys.argv[1])
        for turn, _, speaker in diarization.itertracks(yield_label=True):
            print(f"{turn.start:.3f} {turn.end:.3f} {speaker}")
        """;
}
