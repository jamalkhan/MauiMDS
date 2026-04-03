using MauiMds.Models;
using MauiMds.Services;
using Microsoft.Extensions.Logging;

namespace MauiMds.Features.Editor;

public sealed class EditorModeSupportController
{
    private readonly SnackbarService _snackbarService;
    private readonly ILogger<EditorModeSupportController> _logger;

    public EditorModeSupportController(SnackbarService snackbarService, ILogger<EditorModeSupportController> logger)
    {
        _snackbarService = snackbarService;
        _logger = logger;
    }

    public bool IsVisualEditorSupported => DeviceInfo.Current.Platform == DevicePlatform.MacCatalyst;

    public string VisualEditorUnavailableMessage => "Coming Soon: Visual Editor is currently available on macOS only.";

    public EditorViewMode ResolveSupportedViewMode(EditorViewMode requestedMode, bool showUnsupportedSnackbar)
    {
        if (requestedMode != EditorViewMode.RichTextEditor || IsVisualEditorSupported)
        {
            return requestedMode;
        }

        if (showUnsupportedSnackbar)
        {
            var message = "Visual Editor is not available on this platform yet. Switched to Text Editor.";
            _snackbarService.EnqueueMessage(SnackbarMessageLevel.Error, nameof(EditorModeSupportController), message);
            _logger.LogWarning("Attempted to activate Visual Editor on unsupported platform {Platform}. Falling back to Text Editor.", DeviceInfo.Current.Platform);
        }

        return EditorViewMode.TextEditor;
    }
}
