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
                TranscriptionEngine = preferences.TranscriptionEngine,
                DiarizationEngine = preferences.DiarizationEngine,
                WhisperModelPath = preferences.WhisperModelPath,
                PyannotePythonPath = preferences.PyannotePythonPath
            };
        }
        catch
        {
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
            WhisperModelPath = preferences.WhisperModelPath,
            PyannotePythonPath = preferences.PyannotePythonPath
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
            FileLogLevel = Microsoft.Extensions.Logging.LogLevel.Information
        };
    }

    private static Microsoft.Extensions.Logging.LogLevel NormalizeLogLevel(Microsoft.Extensions.Logging.LogLevel logLevel)
    {
        return Enum.IsDefined(logLevel) && logLevel != Microsoft.Extensions.Logging.LogLevel.None
            ? logLevel
            : Microsoft.Extensions.Logging.LogLevel.Information;
    }
}
