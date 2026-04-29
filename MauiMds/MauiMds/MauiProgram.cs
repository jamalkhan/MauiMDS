using MauiMds.AudioCapture;
using MauiMds.AudioCapture.MacCatalyst;
using MauiMds.Transcription;
using MauiMds.Features.Editor;
using MauiMds.Features.Export;
using MauiMds.Features.Session;
using MauiMds.Features.Workspace;
using MauiMds.Logging;
using MauiMds.Processors;
using MauiMds.Services;
using MauiMds.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;

namespace MauiMds;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
        var snackbarService = new SnackbarService();
        var preferencesService = new EditorPreferencesService();
        var preferences = preferencesService.Load();
        var fileLogLevelSwitch = new FileLogLevelSwitch(preferences.FileLogLevel);
        var maxLogFileSizeBytes = (long)Math.Max(1, preferences.MaxLogFileSizeMb) * 1024 * 1024;

		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			})
			.ConfigureLifecycleEvents(events =>
			{
#if MACCATALYST
				// Set the terminating flag before UIKit begins dismantling the view
				// hierarchy. Without this, _traitCollectionDidChange: callbacks fire
				// on managed views during teardown, and any exception they throw
				// escapes to ObjC and triggers SIGABRT via xamarin_process_managed_exception.
				events.AddiOS(ios => ios.WillTerminate(_ => App.IsTerminating = true));
#endif
			})
#if MACCATALYST
			.ConfigureMauiHandlers(handlers =>
			{
				handlers.AddHandler(typeof(Microsoft.Maui.Controls.Editor),
					typeof(MauiMds.Platforms.MacCatalyst.ShortcutAwareEditorHandler));
			})
#endif
			;

		builder.Logging.ClearProviders();
		builder.Logging.AddDebug();
		builder.Logging.AddProvider(new FileLoggerProvider(LogPaths.AppLogFilePath, fileLogLevelSwitch, maxLogFileSizeBytes));
        builder.Logging.AddProvider(new SnackbarLoggerProvider(snackbarService, LogLevel.Information));

		// Register our services for Dependency Injection
		builder.Services.AddSingleton(snackbarService);
		builder.Services.AddSingleton<MdsParser>();
		builder.Services.AddSingleton<IEditorPreferencesService>(preferencesService);
		builder.Services.AddSingleton<IClock, SystemClock>();
		builder.Services.AddSingleton<IDelayScheduler, TaskDelayScheduler>();
		builder.Services.AddSingleton(fileLogLevelSwitch);
		builder.Services.AddSingleton<IDocumentWatchService, DocumentWatchService>();
		builder.Services.AddSingleton<ISessionStateService, SessionStateService>();
		builder.Services.AddSingleton<MarkdownFileAccessService>();
		builder.Services.AddSingleton<IMarkdownFileAccessService>(sp => sp.GetRequiredService<MarkdownFileAccessService>());
		builder.Services.AddSingleton<IMarkdownFileStorageService, MarkdownFileStorageService>();
		builder.Services.AddSingleton<IMarkdownDocumentPickerService, MarkdownDocumentPickerService>();
		builder.Services.AddSingleton<IMarkdownDocumentService, MarkdownDocumentService>();
		builder.Services.AddSingleton<IWorkspaceBrowserService, WorkspaceBrowserService>();
		builder.Services.AddSingleton<WorkspaceExplorerState>();
		builder.Services.AddSingleton<DocumentWorkflowController>();
		builder.Services.AddSingleton<DocumentApplyController>();
		builder.Services.AddSingleton<PreviewPipelineController>();
		builder.Services.AddSingleton<EditorModeSupportController>();
		builder.Services.AddSingleton<AutosaveCoordinator>();
		builder.Services.AddSingleton<IPlatformInfo, SystemPlatformInfo>();
		builder.Services.AddSingleton<SessionRestoreCoordinator>();
		builder.Services.AddSingleton<IPdfExportService, PdfExportService>();
		builder.Services.AddSingleton<IAudioCaptureService, AudioCaptureService>();
		builder.Services.AddSingleton<IAudioPlayerService, AudioPlayerService>();
		builder.Services.AddSingleton<ITranscriptionPipelineFactory, TranscriptionPipelineFactory>();
		builder.Services.AddSingleton<MainViewModel>();
		builder.Services.AddSingleton<Views.MainPage>();

		var app = builder.Build();
		var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
		logger.LogInformation("MAUI app built. Log file: {LogFilePath}", LogPaths.AppLogFilePath);

		return app;
	}
}
