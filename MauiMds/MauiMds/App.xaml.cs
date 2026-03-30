using MauiMds.Logging;
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
        _logger.LogInformation("Creating application window.");

        try
        {
            var rootPage = new NavigationPage(_mainPage);
            AttachMenuBar(rootPage);
            _logger.LogInformation("Root page created successfully.");
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
        fileMenu.Add(CreateMenuItem("New", viewModel.NewDocumentCommand));
        fileMenu.Add(CreateMenuItem("Open", viewModel.OpenFileCommand));
        fileMenu.Add(CreateMenuItem("Save", viewModel.SaveCommand));
        fileMenu.Add(CreateMenuItem("Save As", viewModel.SaveAsCommand));
        fileMenu.Add(CreateMenuItem("Revert", viewModel.RevertCommand));
        fileMenu.Add(CreateMenuItem("Close", viewModel.CloseDocumentCommand));

        var viewMenu = new MenuBarItem { Text = "View" };
        viewMenu.Add(CreateMenuItem("Read-Only Viewer", viewModel.SetViewModeCommand, Models.EditorViewMode.Viewer));
        viewMenu.Add(CreateMenuItem("Markdown Editor", viewModel.SetViewModeCommand, Models.EditorViewMode.TextEditor));
        viewMenu.Add(CreateMenuItem("Rich Text Editor", viewModel.SetViewModeCommand, Models.EditorViewMode.RichTextEditor));

        var toolsMenu = new MenuBarItem { Text = "Tools" };
        toolsMenu.Add(CreateMenuItem("Preferences", viewModel.ShowPreferencesCommand));

        rootPage.MenuBarItems.Add(fileMenu);
        rootPage.MenuBarItems.Add(viewMenu);
        rootPage.MenuBarItems.Add(toolsMenu);
        _logger.LogInformation("Attached File, View, and Tools menus to the root navigation page.");
    }

    private static MenuFlyoutItem CreateMenuItem(string text, ICommand command, object? commandParameter = null)
    {
        return new MenuFlyoutItem
        {
            Text = text,
            Command = command,
            CommandParameter = commandParameter
        };
    }
}
