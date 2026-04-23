using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace MauiMds.Transcription.Engines.Pyannote;

/// <summary>
/// Diarization adapter that shells out to a local Python environment running pyannote.audio 3.x.
/// An inline Python script is written to a temp file, executed, and its stdout parsed as
/// whitespace-delimited "start end speaker" lines.
/// </summary>
public sealed class PyannoteDiarizationEngine : IDiarizationEngine
{
    private readonly string _pythonPath;
    private readonly ILogger _logger;

    public string Name => "pyannote.audio";

    public bool IsAvailable =>
        !string.IsNullOrWhiteSpace(_pythonPath) && File.Exists(_pythonPath);

    public PyannoteDiarizationEngine(string pythonPath, ILogger<PyannoteDiarizationEngine> logger)
    {
        _pythonPath = pythonPath;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SpeakerSegment>> DiarizeAsync(
        string audioFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException(
                "pyannote.audio is not configured. " +
                "Set the Python executable path in Preferences > Transcription.");
        }

        var scriptPath = Path.Combine(Path.GetTempPath(), $"mauimds_pyannote_{Guid.NewGuid():N}.py");

        try
        {
            await File.WriteAllTextAsync(scriptPath, PyannoteScript, cancellationToken);

            _logger.LogInformation("Pyannote: starting diarization on {File}.", audioFilePath);
            progress?.Report(0.05);

            var stdout = await RunPythonAsync(scriptPath, audioFilePath, cancellationToken);

            progress?.Report(0.95);

            var segments = ParseOutput(stdout);
            progress?.Report(1.0);
            _logger.LogInformation("Pyannote: diarization complete — {Count} speaker segments.", segments.Count);
            return segments;
        }
        finally
        {
            if (File.Exists(scriptPath))
            {
                try { File.Delete(scriptPath); } catch { /* best-effort cleanup */ }
            }
        }
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
                "Ensure pyannote.audio 3.x is installed and a Hugging Face token is configured " +
                "(run: huggingface-cli login).");
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

    // Printed to a temp .py file and executed by the user-configured Python interpreter.
    // Requires: pip install pyannote.audio  +  huggingface-cli login
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
