using System.Text.Json;
using MauiMds.Logging;
using MauiMds.Models;

namespace MauiMds.Services;

public sealed class EditorPreferencesService : IEditorPreferencesService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public EditorPreferences Load()
    {
        try
        {
            if (!File.Exists(LogPaths.PreferencesFilePath))
            {
                return CreateDefaultPreferences();
            }

            var json = File.ReadAllText(LogPaths.PreferencesFilePath);
            var preferences = JsonSerializer.Deserialize<EditorPreferences>(json, JsonOptions) ?? CreateDefaultPreferences();
            return new EditorPreferences
            {
                AutoSaveEnabled = preferences.AutoSaveEnabled,
                AutoSaveDelaySeconds = Math.Max(5, preferences.AutoSaveDelaySeconds),
                MaxLogFileSizeMb = Math.Max(1, preferences.MaxLogFileSizeMb),
                InitialViewerRenderLineCount = Math.Max(5, preferences.InitialViewerRenderLineCount),
                Use24HourTime = preferences.Use24HourTime,
                FileLogLevel = NormalizeLogLevel(preferences.FileLogLevel),
                KeyboardShortcuts = preferences.KeyboardShortcuts ?? EditorPreferences.DefaultShortcuts,
                TranscriptionEngine = NormalizeTranscriptionEngine(preferences.TranscriptionEngine),
                DiarizationEngine = preferences.DiarizationEngine,
                WhisperBinaryPath = preferences.WhisperBinaryPath,
                WhisperModelPath = preferences.WhisperModelPath,
                PyannotePythonPath = preferences.PyannotePythonPath,
                PyannoteHfToken = preferences.PyannoteHfToken,
                RecordingFormat = preferences.RecordingFormat,
                WorkspaceRefreshIntervalSeconds = Math.Max(0, preferences.WorkspaceRefreshIntervalSeconds)
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"EditorPreferencesService: failed to load preferences, using defaults. {ex}");
            return CreateDefaultPreferences();
        }
    }

    public void Save(EditorPreferences preferences)
    {
        var directory = Path.GetDirectoryName(LogPaths.PreferencesFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var normalized = new EditorPreferences
        {
            AutoSaveEnabled = preferences.AutoSaveEnabled,
            AutoSaveDelaySeconds = Math.Max(5, preferences.AutoSaveDelaySeconds),
            MaxLogFileSizeMb = Math.Max(1, preferences.MaxLogFileSizeMb),
            InitialViewerRenderLineCount = Math.Max(5, preferences.InitialViewerRenderLineCount),
            Use24HourTime = preferences.Use24HourTime,
            FileLogLevel = NormalizeLogLevel(preferences.FileLogLevel),
            KeyboardShortcuts = preferences.KeyboardShortcuts ?? EditorPreferences.DefaultShortcuts,
            TranscriptionEngine = preferences.TranscriptionEngine,
            DiarizationEngine = preferences.DiarizationEngine,
            WhisperBinaryPath = preferences.WhisperBinaryPath,
            WhisperModelPath = preferences.WhisperModelPath,
            PyannotePythonPath = preferences.PyannotePythonPath,
            PyannoteHfToken = preferences.PyannoteHfToken,
            RecordingFormat = preferences.RecordingFormat,
            WorkspaceRefreshIntervalSeconds = Math.Max(0, preferences.WorkspaceRefreshIntervalSeconds)
        };

        var json = JsonSerializer.Serialize(normalized, JsonOptions);
        File.WriteAllText(LogPaths.PreferencesFilePath, json);
    }

    private static EditorPreferences CreateDefaultPreferences()
    {
        return new EditorPreferences
        {
            AutoSaveEnabled = true,
            AutoSaveDelaySeconds = 30,
            MaxLogFileSizeMb = 2,
            InitialViewerRenderLineCount = 20,
            Use24HourTime = false,
            FileLogLevel = Microsoft.Extensions.Logging.LogLevel.Information,
#if WINDOWS
            TranscriptionEngine = TranscriptionEngineType.WhisperCpp,
#endif
        };
    }

    // Apple Speech is Mac Catalyst only; silently fall back to WhisperCpp on Windows.
    private static TranscriptionEngineType NormalizeTranscriptionEngine(TranscriptionEngineType engine)
    {
#if WINDOWS
        if (engine == TranscriptionEngineType.AppleSpeech)
            return TranscriptionEngineType.WhisperCpp;
#endif
        return engine;
    }

    private static Microsoft.Extensions.Logging.LogLevel NormalizeLogLevel(Microsoft.Extensions.Logging.LogLevel logLevel)
    {
        return Enum.IsDefined(logLevel) && logLevel != Microsoft.Extensions.Logging.LogLevel.None
            ? logLevel
            : Microsoft.Extensions.Logging.LogLevel.Information;
    }
}
