using MauiMds.Logging;
using MauiMds.Processors;
using MauiMds.Services;
using MauiMds.ViewModels;
using Microsoft.Extensions.Logging;

namespace MauiMds;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
        var snackbarService = new SnackbarService();

		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		builder.Logging.ClearProviders();
		builder.Logging.AddDebug();
		builder.Logging.AddProvider(new FileLoggerProvider(LogPaths.AppLogFilePath, LogLevel.Debug));
        builder.Logging.AddProvider(new SnackbarLoggerProvider(snackbarService, LogLevel.Information));

		// Register our services for Dependency Injection
		builder.Services.AddSingleton(snackbarService);
		builder.Services.AddSingleton<MdsParser>();
		builder.Services.AddSingleton<IMarkdownDocumentService, MarkdownDocumentService>();
		builder.Services.AddSingleton<MainViewModel>();
		builder.Services.AddSingleton<Views.MainPage>();

		var app = builder.Build();
		var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
		logger.LogInformation("MAUI app built. Log file: {LogFilePath}", LogPaths.AppLogFilePath);

		return app;
	}
}
