using MauiMds.AudioCapture;
#if MACCATALYST
using MauiMds.AudioCapture.MacCatalyst;
#elif WINDOWS
using MauiMds.AudioCapture.Windows;
#endif
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

        // Bootstrap: read the two values needed to configure logging before the DI
        // container exists. The EditorPreferencesService instance is throwaway — DI
        // will own a fresh one after Build(). snackbarService and fileLogLevelSwitch
        // must be pre-constructed because they're passed into logger providers AND
        // need to be the same objects the rest of the app resolves from DI.
        var bootPrefs = new EditorPreferencesService().Load();
        var snackbarService = new SnackbarService();
        var fileLogLevelSwitch = new FileLogLevelSwitch(bootPrefs.FileLogLevel);
        var maxLogFileSizeBytes = (long)Math.Max(1, bootPrefs.MaxLogFileSizeMb) * 1024 * 1024;

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
		builder.Services.AddSingleton<ISnackbarService>(snackbarService);
		builder.Services.AddSingleton<MdsParser>();
		builder.Services.AddSingleton<IEditorPreferencesService, EditorPreferencesService>();
		builder.Services.AddSingleton<IClock, SystemClock>();
		builder.Services.AddSingleton<IDelayScheduler, TaskDelayScheduler>();
		builder.Services.AddSingleton(fileLogLevelSwitch);
		builder.Services.AddSingleton<IDocumentWatchService, DocumentWatchService>();
		builder.Services.AddSingleton<ISessionStateService, SessionStateService>();
#if MACCATALYST
		builder.Services.AddSingleton<MacMarkdownFileAccessService>();
		builder.Services.AddSingleton<IMarkdownFileAccessService>(sp => sp.GetRequiredService<MacMarkdownFileAccessService>());
		builder.Services.AddSingleton<IDocumentPickerPlatformService, DocumentPickerPlatformService>();
#else
		builder.Services.AddSingleton<IMarkdownFileAccessService, MarkdownFileAccessService>();
		builder.Services.AddSingleton<IDocumentPickerPlatformService, DocumentPickerPlatformService>();
#endif
		builder.Services.AddSingleton<IFolderPickerPlatformService, FolderPickerPlatformService>();
		builder.Services.AddSingleton<IMarkdownFileStorageService, MarkdownFileStorageService>();
		builder.Services.AddSingleton<IMarkdownDocumentPickerService, MarkdownDocumentPickerService>();
		builder.Services.AddSingleton<IMarkdownDocumentService, MarkdownDocumentService>();
		builder.Services.AddSingleton<IWorkspaceBrowserService, WorkspaceBrowserService>();
		builder.Services.AddSingleton<WorkspaceExplorerState>();
		builder.Services.AddSingleton<IMainThreadDispatcher, MauiMainThreadDispatcher>();
		builder.Services.AddSingleton<IDocumentWorkflowService, DocumentWorkflowService>();
		builder.Services.AddSingleton<IDocumentApplyService, DocumentApplyService>();
		builder.Services.AddSingleton<IPreviewPipelineCoordinator, PreviewPipelineCoordinator>();
		builder.Services.AddSingleton<IEditorModeSupportService, EditorModeSupportService>();
		builder.Services.AddSingleton<IAutosaveCoordinator, AutosaveCoordinator>();
		builder.Services.AddSingleton<IPlatformInfo, SystemPlatformInfo>();
		builder.Services.AddSingleton<ISessionRestoreCoordinator, SessionRestoreCoordinator>();
		builder.Services.AddSingleton<IPdfSaveDialogService, PdfSaveDialogService>();
		builder.Services.AddSingleton<IPdfExportService, PdfExportService>();
		builder.Services.AddSingleton<IProcessRunner, SystemProcessRunner>();
#if MACCATALYST
		builder.Services.AddSingleton<IAudioFormatConverter, MacAudioFormatConverter>();
#elif WINDOWS
		builder.Services.AddSingleton<IAudioFormatConverter, WindowsAudioFormatConverter>();
#endif
		builder.Services.AddSingleton<IAudioCaptureService, AudioCaptureService>();
		builder.Services.AddSingleton<IAudioPlayerService, AudioPlayerService>();
		builder.Services.AddSingleton<ISpeakerMergeStrategy, OverlapSpeakerMergeStrategy>();
		builder.Services.AddSingleton<ITranscriptionPipelineFactory, TranscriptionPipelineFactory>();
		builder.Services.AddSingleton<ITranscriptStorage, FileTranscriptStorage>();
		builder.Services.AddSingleton<ITranscriptFormatter, MarkdownTranscriptFormatter>();
		builder.Services.AddSingleton<IFileSystem, RealFileSystem>();
		builder.Services.AddSingleton<IApplicationLifetime, MauiApplicationLifetime>();
		builder.Services.AddSingleton<IAlertService, MauiAlertService>();
		builder.Services.AddSingleton<PreferencesViewModel>();
		builder.Services.AddSingleton<MainViewModel>();
		builder.Services.AddSingleton<Views.MainPage>();

		var app = builder.Build();
		var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
		logger.LogInformation("MAUI app built. Log file: {LogFilePath}", LogPaths.AppLogFilePath);

		return app;
	}
}
