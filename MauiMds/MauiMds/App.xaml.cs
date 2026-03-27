using MauiMds.Logging;
using MauiMds.ViewModels;
using MauiMds.Views;
using Microsoft.Extensions.Logging;

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
        fileMenu.Add(new MenuFlyoutItem
        {
            Text = "Open",
            Command = viewModel.OpenFileCommand
        });

        rootPage.MenuBarItems.Add(fileMenu);
        _logger.LogInformation("Attached File menu to the root navigation page.");
    }
}
