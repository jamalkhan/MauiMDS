using MauiMds.Models;

namespace MauiMds.Features.Editor;

public interface IEditorModeSupportService
{
    bool IsVisualEditorSupported { get; }
    string VisualEditorUnavailableMessage { get; }
    EditorViewMode ResolveSupportedViewMode(EditorViewMode requestedMode, bool showUnsupportedSnackbar);
}
