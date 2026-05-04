using MauiMds.Logging;
using MauiMds.Models;
using MauiMds.ViewModels;
using MauiMds.Views;
using Microsoft.Extensions.Logging;
using System.Windows.Input;

namespace MauiMds;

public partial class App : Application
{
    /// <summary>
    /// Set to true in applicationWillTerminate (before UIKit begins tearing down views).
    /// Managed UI event handlers must guard against running after this point or they risk
    /// throwing inside _traitCollectionDidChange: which causes SIGABRT on Mac Catalyst.
    /// Only assigned on Mac; always false on other platforms.
    /// </summary>
#if MACCATALYST
    internal static volatile bool IsTerminating;
#else
    internal static bool IsTerminating => false;
#endif

    private readonly ILogger<App> _logger;
    private readonly MainPage _mainPage;
    private NavigationPage? _rootPage;
    private MenuBarItem? _formatMenu;

    public App(MainPage mainPage, ILogger<App> logger)
    {
        InitializeComponent();
        _logger = logger;
        _mainPage = mainPage;

        _logger.LogInformation("App initialized. Log file: {LogFilePath}", LogPaths.AppLogFilePath);
        RegisterGlobalExceptionHandlers();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        _logger.LogDebug("Creating application window.");

        try
        {
            _rootPage = new NavigationPage(_mainPage);

            // Build menu bar items into the NavigationPage's observable collection now.
            // MAUI's Mac Catalyst handler picks up the collection when it connects the
            // platform view, so we don't need to wait for any lifecycle event.
            AttachMenuBar(_rootPage);

            if (_mainPage.BindingContext is MainViewModel vm)
            {
                vm.KeyboardShortcutsChanged += OnKeyboardShortcutsChanged;
            }

            _logger.LogDebug("Root page created successfully.");
            return new Window(_rootPage);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Unhandled exception while creating the main window.");
            throw;
        }
    }

    private void OnKeyboardShortcutsChanged(object? sender, EventArgs e)
    {
        if (_rootPage is null || _formatMenu is null)
        {
            return;
        }

        if (_mainPage.BindingContext is not MainViewModel vm)
        {
            return;
        }

        _formatMenu.Clear();
        BuildFormatMenuItems(_formatMenu, vm);
        _logger.LogInformation("Format menu rebuilt after keyboard shortcuts change.");
    }

    private void RegisterGlobalExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                _logger.LogCritical(ex, "AppDomain unhandled exception. IsTerminating: {IsTerminating}", args.IsTerminating);
            }
            else
            {
                _logger.LogCritical("AppDomain unhandled exception with non-exception payload. IsTerminating: {IsTerminating}", args.IsTerminating);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _logger.LogError(args.Exception, "TaskScheduler unobserved task exception.");
        };
    }

    private void AttachMenuBar(NavigationPage rootPage)
    {
        try
        {
            if (_mainPage.BindingContext is not MainViewModel viewModel)
            {
                _logger.LogWarning("Unable to attach menu bar because MainViewModel is missing.");
                return;
            }

            var fileMenu = new MenuBarItem { Text = "File" };
            fileMenu.Add(CreateMenuItem("New", viewModel.NewDocumentCommand, key: "N", primaryModifier: true));
            fileMenu.Add(CreateMenuItem("Open", viewModel.OpenFileCommand, key: "O", primaryModifier: true));
            fileMenu.Add(CreateMenuItem("Save", viewModel.SaveCommand, key: "S", primaryModifier: true));
            fileMenu.Add(CreateMenuItem("Save As", viewModel.SaveAsCommand, key: "S", primaryModifier: true, includeShift: true));
            fileMenu.Add(CreateMenuItem("Revert", viewModel.RevertCommand));
            fileMenu.Add(CreateMenuItem("Close", viewModel.CloseDocumentCommand, key: "W", primaryModifier: true));

            var editMenu = new MenuBarItem { Text = "Edit" };
            editMenu.Add(CreateMenuItem("Undo", viewModel.UndoCommand, key: "Z", primaryModifier: true));
            editMenu.Add(CreateMenuItem("Redo", viewModel.RedoCommand, key: "Z", primaryModifier: true, includeShift: true));
            editMenu.Add(CreateMenuItem("Cut", viewModel.CutCommand, key: "X", primaryModifier: true));
            editMenu.Add(CreateMenuItem("Copy", viewModel.CopyCommand, key: "C", primaryModifier: true));
            editMenu.Add(CreateMenuItem("Paste", viewModel.PasteCommand, key: "V", primaryModifier: true));
            editMenu.Add(CreateMenuItem("Find", viewModel.FindCommand, key: "F", primaryModifier: true));

            _formatMenu = new MenuBarItem { Text = "Format" };
            BuildFormatMenuItems(_formatMenu, viewModel);

            var viewMenu = new MenuBarItem { Text = "View" };
            viewMenu.Add(CreateMenuItem("Reader", viewModel.SetViewModeCommand, EditorViewMode.Viewer));
            viewMenu.Add(CreateMenuItem("Text Editor", viewModel.SetViewModeCommand, EditorViewMode.TextEditor));
            viewMenu.Add(CreateMenuItem("Visual Editor", viewModel.SetViewModeCommand, EditorViewMode.RichTextEditor, isEnabled: viewModel.IsVisualEditorSupported));

            var toolsMenu = new MenuBarItem { Text = "Tools" };
            toolsMenu.Add(CreateMenuItem("Preferences", viewModel.Preferences.ShowPreferencesCommand));

            rootPage.MenuBarItems.Add(fileMenu);
            rootPage.MenuBarItems.Add(editMenu);
            rootPage.MenuBarItems.Add(_formatMenu);
            rootPage.MenuBarItems.Add(viewMenu);
            rootPage.MenuBarItems.Add(toolsMenu);
            _logger.LogInformation("Menu bar attached: File, Edit, Format, View, Tools.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to attach menu bar.");
        }
    }

    private void BuildFormatMenuItems(MenuBarItem formatMenu, MainViewModel viewModel)
    {
        var shortcuts = viewModel.Preferences.CurrentShortcuts;
        formatMenu.Add(CreateMenuItem("Paragraph", viewModel.FormatParagraphCommand));
        formatMenu.Add(CreateMenuItem("H1", viewModel.FormatHeader1Command, key: GetShortcutKey(shortcuts, EditorActionType.Header1, "1"), primaryModifier: true));
        formatMenu.Add(CreateMenuItem("H2", viewModel.FormatHeader2Command, key: GetShortcutKey(shortcuts, EditorActionType.Header2, "2"), primaryModifier: true));
        formatMenu.Add(CreateMenuItem("H3", viewModel.FormatHeader3Command, key: GetShortcutKey(shortcuts, EditorActionType.Header3, "3"), primaryModifier: true));
        formatMenu.Add(CreateMenuItem("Bullet", viewModel.FormatBulletCommand));
        formatMenu.Add(CreateMenuItem("Checklist", viewModel.FormatChecklistCommand));
        formatMenu.Add(CreateMenuItem("Quote", viewModel.FormatQuoteCommand));
        formatMenu.Add(CreateMenuItem("Code", viewModel.FormatCodeCommand));
        formatMenu.Add(CreateMenuItem("Bold", viewModel.FormatBoldCommand, key: GetShortcutKey(shortcuts, EditorActionType.Bold, "B"), primaryModifier: true));
        formatMenu.Add(CreateMenuItem("Italic", viewModel.FormatItalicCommand, key: GetShortcutKey(shortcuts, EditorActionType.Italic, "I"), primaryModifier: true));
    }

    private static string? GetShortcutKey(IReadOnlyList<KeyboardShortcutDefinition> shortcuts, EditorActionType action, string fallback)
    {
        var key = shortcuts.FirstOrDefault(s => s.Action == action)?.Key;
        return string.IsNullOrWhiteSpace(key) ? fallback : key;
    }

    private static MenuFlyoutItem CreateMenuItem(string text, ICommand command, object? commandParameter = null, string? key = null, bool primaryModifier = false, bool includeShift = false, bool isEnabled = true)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            Command = command,
            CommandParameter = commandParameter,
            IsEnabled = isEnabled
        };

        if (!string.IsNullOrWhiteSpace(key))
        {
            var modifiers = KeyboardAcceleratorModifiers.None;
            if (primaryModifier)
            {
                modifiers |= DeviceInfo.Current.Platform == DevicePlatform.MacCatalyst
                    ? KeyboardAcceleratorModifiers.Cmd
                    : KeyboardAcceleratorModifiers.Ctrl;
            }

            if (includeShift)
            {
                modifiers |= KeyboardAcceleratorModifiers.Shift;
            }

            item.KeyboardAccelerators.Add(new KeyboardAccelerator
            {
                Key = key,
                Modifiers = modifiers
            });
        }

        return item;
    }
}
