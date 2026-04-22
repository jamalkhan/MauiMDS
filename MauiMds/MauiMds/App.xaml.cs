using MauiMds.Logging;
using MauiMds.Models;
using MauiMds.ViewModels;
using MauiMds.Views;
using Microsoft.Extensions.Logging;
using System.Windows.Input;

namespace MauiMds;

public partial class App : Application
{
    private readonly ILogger<App> _logger;
    private readonly MainPage _mainPage;

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
            var rootPage = new NavigationPage(_mainPage);
#if MACCATALYST
            _logger.LogWarning("Skipping custom menu bar attachment on Mac Catalyst due to startup scene instability.");
#else
            AttachMenuBar(rootPage);
#endif
            _logger.LogDebug("Root page created successfully.");
            return new Window(rootPage);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Unhandled exception while creating the main window.");
            throw;
        }
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

        var formatMenu = new MenuBarItem { Text = "Format" };
        formatMenu.Add(CreateMenuItem("Paragraph", viewModel.FormatParagraphCommand));
        formatMenu.Add(CreateMenuItem("H1", viewModel.FormatHeader1Command, key: "1", primaryModifier: true));
        formatMenu.Add(CreateMenuItem("H2", viewModel.FormatHeader2Command, key: "2", primaryModifier: true));
        formatMenu.Add(CreateMenuItem("H3", viewModel.FormatHeader3Command, key: "3", primaryModifier: true));
        formatMenu.Add(CreateMenuItem("Bullet", viewModel.FormatBulletCommand));
        formatMenu.Add(CreateMenuItem("Checklist", viewModel.FormatChecklistCommand));
        formatMenu.Add(CreateMenuItem("Quote", viewModel.FormatQuoteCommand));
        formatMenu.Add(CreateMenuItem("Code", viewModel.FormatCodeCommand));

        var viewMenu = new MenuBarItem { Text = "View" };
        viewMenu.Add(CreateMenuItem("Reader", viewModel.SetViewModeCommand, EditorViewMode.Viewer));
        viewMenu.Add(CreateMenuItem("Text Editor", viewModel.SetViewModeCommand, EditorViewMode.TextEditor));
        viewMenu.Add(CreateMenuItem("Visual Editor", viewModel.SetViewModeCommand, EditorViewMode.RichTextEditor, isEnabled: viewModel.IsVisualEditorSupported));

        var toolsMenu = new MenuBarItem { Text = "Tools" };
        toolsMenu.Add(CreateMenuItem("Preferences", viewModel.ShowPreferencesCommand));

        rootPage.MenuBarItems.Add(fileMenu);
        rootPage.MenuBarItems.Add(editMenu);
        rootPage.MenuBarItems.Add(formatMenu);
        rootPage.MenuBarItems.Add(viewMenu);
        rootPage.MenuBarItems.Add(toolsMenu);
        _logger.LogDebug("Attached File, Edit, Format, View, and Tools menus to the root navigation page.");
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
