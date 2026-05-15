using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace Rizedown.Platforms.MacCatalyst;

internal sealed class ShortcutAwareEditorHandler : EditorHandler
{
    protected override MauiTextView CreatePlatformView() => new ShortcutAwareTextView();
}
