using MauiMds.Models;
using MauiMds.Services;
using Microsoft.Extensions.Logging;

namespace MauiMds.Features.Editor;

public sealed class EditorModeSupportService
{
    private readonly ISnackbarService _snackbarService;
    private readonly ILogger<EditorModeSupportService> _logger;

    public EditorModeSupportService(ISnackbarService snackbarService, ILogger<EditorModeSupportService> logger)
    {
        _snackbarService = snackbarService;
        _logger = logger;
    }

    public bool IsVisualEditorSupported =>
        DeviceInfo.Current.Platform == DevicePlatform.MacCatalyst ||
        DeviceInfo.Current.Platform == DevicePlatform.WinUI;

    public string VisualEditorUnavailableMessage => "Visual Editor is not available on this platform.";

    public EditorViewMode ResolveSupportedViewMode(EditorViewMode requestedMode, bool showUnsupportedSnackbar)
    {
        if (requestedMode != EditorViewMode.RichTextEditor || IsVisualEditorSupported)
        {
            return requestedMode;
        }

        if (showUnsupportedSnackbar)
        {
            var message = "Visual Editor is not available on this platform yet. Switched to Text Editor.";
            _snackbarService.EnqueueMessage(SnackbarMessageLevel.Error, nameof(EditorModeSupportService), message);
            _logger.LogWarning("Attempted to activate Visual Editor on unsupported platform {Platform}. Falling back to Text Editor.", DeviceInfo.Current.Platform);
        }

        return EditorViewMode.TextEditor;
    }
}
