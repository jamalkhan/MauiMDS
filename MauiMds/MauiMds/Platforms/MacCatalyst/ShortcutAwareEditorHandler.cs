using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace MauiMds.Platforms.MacCatalyst;

internal sealed class ShortcutAwareEditorHandler : EditorHandler
{
    protected override MauiTextView CreatePlatformView() => new ShortcutAwareTextView();
}
