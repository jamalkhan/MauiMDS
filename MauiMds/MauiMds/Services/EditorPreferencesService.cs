using MauiMds.Models;

namespace MauiMds.Services;

public sealed class EditorPreferencesService : IEditorPreferencesService
{
    private const string AutoSaveEnabledKey = "editor.autosave.enabled";
    private const string AutoSaveDelaySecondsKey = "editor.autosave.delay-seconds";

    public EditorPreferences Load()
    {
        var delay = Preferences.Default.Get(AutoSaveDelaySecondsKey, 30);
        if (delay < 5)
        {
            delay = 5;
        }

        return new EditorPreferences
        {
            AutoSaveEnabled = Preferences.Default.Get(AutoSaveEnabledKey, true),
            AutoSaveDelaySeconds = delay
        };
    }

    public void Save(EditorPreferences preferences)
    {
        Preferences.Default.Set(AutoSaveEnabledKey, preferences.AutoSaveEnabled);
        Preferences.Default.Set(AutoSaveDelaySecondsKey, Math.Max(5, preferences.AutoSaveDelaySeconds));
    }
}
