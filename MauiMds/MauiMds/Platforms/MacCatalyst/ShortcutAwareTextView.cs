using Foundation;
using Microsoft.Maui.Platform;
using UIKit;
using MauiMds.Models;
using MauiMds.ViewModels;

namespace MauiMds.Platforms.MacCatalyst;

internal sealed class ShortcutAwareTextView : MauiTextView
{
    public override void PressesBegan(NSSet<UIPress> presses, UIPressesEvent? evt)
    {
        if (!TryDispatchFormatShortcut(presses))
            base.PressesBegan(presses, evt!);
    }

    private static bool TryDispatchFormatShortcut(NSSet<UIPress> presses)
    {
        foreach (var obj in presses)
        {
            if (obj is not UIPress { Key: { } key })
                continue;

            const UIKeyModifierFlags interestingModifiers =
                UIKeyModifierFlags.Command | UIKeyModifierFlags.Shift |
                UIKeyModifierFlags.Alternate | UIKeyModifierFlags.Control;

            if ((key.ModifierFlags & interestingModifiers) != UIKeyModifierFlags.Command)
                continue;

            var pressedKey = key.Characters?.ToUpperInvariant();
            if (string.IsNullOrEmpty(pressedKey))
                continue;

            var vm = IPlatformApplication.Current?.Services
                         .GetService(typeof(MainViewModel)) as MainViewModel;
            if (vm is null)
                continue;

            var shortcut = vm.Preferences.CurrentShortcuts.FirstOrDefault(s =>
                string.Equals(s.Key, pressedKey, StringComparison.OrdinalIgnoreCase));
            if (shortcut is null)
                continue;

            var command = shortcut.Action switch
            {
                EditorActionType.Header1 => vm.FormatHeader1Command,
                EditorActionType.Header2 => vm.FormatHeader2Command,
                EditorActionType.Header3 => vm.FormatHeader3Command,
                EditorActionType.Bold    => vm.FormatBoldCommand,
                EditorActionType.Italic  => vm.FormatItalicCommand,
                _                        => null
            };

            if (command is null)
                continue;

            MainThread.BeginInvokeOnMainThread(() => command.Execute(null));
            return true;
        }

        return false;
    }
}
