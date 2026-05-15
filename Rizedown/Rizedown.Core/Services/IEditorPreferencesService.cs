using Rizedown.Models;

namespace Rizedown.Services;

public interface IEditorPreferencesService
{
    EditorPreferences Load();
    void Save(EditorPreferences preferences);
}
