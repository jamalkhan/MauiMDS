using MauiMds.Models;

namespace MauiMds.Services;

public interface IEditorPreferencesService
{
    EditorPreferences Load();
    void Save(EditorPreferences preferences);
}
