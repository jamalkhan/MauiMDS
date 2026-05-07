using MauiMds;
using MauiMds.Features.Editor;
using MauiMds.Features.Session;
using MauiMds.Features.Workspace;
using MauiMds.Logging;
using MauiMds.Models;
using MauiMds.Services;
using MauiMds.Tests.TestHelpers;
using MauiMds.Transcription;
using MauiMds.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace MauiMds.Tests.Features.Editor;

[TestClass]
public sealed class MainViewModelTests
{
    private FakeMarkdownDocumentService _documentService = null!;
    private FakeWorkspaceBrowserService _workspaceBrowserService = null!;
    private FakeDocumentWatchService _documentWatchService = null!;
    private FakeAutosaveCoordinator _autosaveCoordinator = null!;
    private FakeSessionRestoreCoordinator _sessionRestoreCoordinator = null!;
    private FakePreviewPipelineCoordinator _previewPipelineCoordinator = null!;
    private FakeEditorModeSupportService _editorModeSupportService = null!;
    private FakePdfExportService _pdfExportService = null!;
    private FakeAudioCaptureService _audioCaptureService = null!;
    private FakeAudioPlayerService _audioPlayerService = null!;
    private FakeTranscriptionPipelineFactory _pipelineFactory = null!;
    private FakeSynchronousDispatcher _dispatcher = null!;
    private FakeApplicationLifetime _applicationLifetime = null!;
    private FakeAlertService _alertService = null!;
    private FakePlatformInfo _platformInfo = null!;
    private PreferencesViewModel _preferences = null!;
    private MainViewModel _vm = null!;

    [TestInitialize]
    public void Setup()
    {
        _documentService = new FakeMarkdownDocumentService();
        _workspaceBrowserService = new FakeWorkspaceBrowserService();
        _documentWatchService = new FakeDocumentWatchService();
        _autosaveCoordinator = new FakeAutosaveCoordinator();
        _sessionRestoreCoordinator = new FakeSessionRestoreCoordinator();
        _previewPipelineCoordinator = new FakePreviewPipelineCoordinator();
        _editorModeSupportService = new FakeEditorModeSupportService();
        _pdfExportService = new FakePdfExportService();
        _audioCaptureService = new FakeAudioCaptureService();
        _audioPlayerService = new FakeAudioPlayerService();
        _pipelineFactory = new FakeTranscriptionPipelineFactory();
        _dispatcher = new FakeSynchronousDispatcher();
        _applicationLifetime = new FakeApplicationLifetime();
        _alertService = new FakeAlertService();
        _platformInfo = new FakePlatformInfo();

        var fileLogLevelSwitch = new FileLogLevelSwitch(Microsoft.Extensions.Logging.LogLevel.Information);
        _preferences = new PreferencesViewModel(
            new FakeEditorPreferencesService(),
            fileLogLevelSwitch,
            _platformInfo,
            _applicationLifetime);

        var workspace = new WorkspaceExplorerState(
            _workspaceBrowserService,
            _dispatcher,
            NullLogger<WorkspaceExplorerState>.Instance);

        var documentWorkflowService = new FakeDocumentWorkflowService();

        _vm = new MainViewModel(
            _documentService,
            _workspaceBrowserService,
            _preferences,
            _documentWatchService,
            new FakeClock(),
            workspace,
            new FakeDocumentApplyService(documentWorkflowService),
            documentWorkflowService,
            _previewPipelineCoordinator,
            _editorModeSupportService,
            _autosaveCoordinator,
            _sessionRestoreCoordinator,
            _pdfExportService,
            _audioCaptureService,
            _audioPlayerService,
            _pipelineFactory,
            new FakeTranscriptStorage(),
            new FakeTranscriptFormatter(),
            new FakeSpeakerMergeStrategy(),
            new FakeFileSystem(),
            NullLoggerFactory.Instance,
            _dispatcher,
            _applicationLifetime,
            _alertService,
            NullLogger<MainViewModel>.Instance);
    }

    [TestCleanup]
    public void Cleanup() { }  // MainViewModel is not IDisposable; resources cleaned up by coordinators

    // ── Initial state ──────────────────────────────────────────────────────────

    [TestMethod]
    public void InitialState_IsUntitled()
    {
        Assert.IsTrue(_vm.IsUntitled);
    }

    [TestMethod]
    public void InitialState_IsNotDirty()
    {
        Assert.IsFalse(_vm.IsDirty);
    }

    [TestMethod]
    public void InitialState_IsBusyFalse()
    {
        Assert.IsFalse(_vm.IsBusy);
    }

    [TestMethod]
    public void InitialState_HasNoWorkspaceRoot()
    {
        Assert.IsFalse(_vm.HasWorkspaceRoot);
    }

    [TestMethod]
    public void InitialState_InlineErrorMessageEmpty()
    {
        Assert.AreEqual(string.Empty, _vm.InlineErrorMessage);
    }

    [TestMethod]
    public void InitialState_EditorTextEmpty()
    {
        Assert.AreEqual(string.Empty, _vm.EditorText);
    }

    // ── PropertyChanged ────────────────────────────────────────────────────────

    [TestMethod]
    public void PropertyChanged_FiresForEditorText()
    {
        var fired = new List<string?>();
        _vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        _vm.EditorText = "hello world";

        CollectionAssert.Contains(fired, nameof(MainViewModel.EditorText));
    }

    [TestMethod]
    public void PropertyChanged_FiresForInlineErrorMessage_ViaPreferencesSaveError()
    {
        var fired = new List<string?>();
        _vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        // PreferencesViewModel.SaveError routes through ReportErrorAsync which sets InlineErrorMessage
        _preferences.ShowPreferencesCommand.Execute(null);
        _preferences.PreferencesAutoSaveDelaySecondsText = "1"; // invalid: < 5
        _preferences.SavePreferencesCommand.Execute(null);

        CollectionAssert.Contains(fired, nameof(MainViewModel.InlineErrorMessage));
    }

    [TestMethod]
    public void PropertyChanged_SkippedWhenIsTerminating()
    {
        _applicationLifetime.IsTerminating = true;
        var fired = new List<string?>();
        _vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        // EditorText setter calls OnPropertyChanged
        _vm.EditorText = "ignored";

        Assert.AreEqual(0, fired.Count);
    }

    // ── SetViewMode ────────────────────────────────────────────────────────────

    [TestMethod]
    public void SetViewMode_UpdatesSelectedViewMode()
    {
        _vm.SetViewModeCommand.Execute(EditorViewMode.TextEditor);
        Assert.AreEqual(EditorViewMode.TextEditor, _vm.SelectedViewMode);
    }

    [TestMethod]
    public void SetViewMode_ToViewer_UpdatesSelectedViewMode()
    {
        _vm.SetViewModeCommand.Execute(EditorViewMode.TextEditor);
        _vm.SetViewModeCommand.Execute(EditorViewMode.Viewer);
        Assert.AreEqual(EditorViewMode.Viewer, _vm.SelectedViewMode);
    }

    // ── WorkspacePanel ─────────────────────────────────────────────────────────

    [TestMethod]
    public void ToggleWorkspacePanel_TogglesVisibility()
    {
        var initial = _vm.IsWorkspacePanelVisible;
        _vm.ToggleWorkspacePanelCommand.Execute(null);
        Assert.AreNotEqual(initial, _vm.IsWorkspacePanelVisible);
    }

    [TestMethod]
    public void ToggleWorkspacePanel_Twice_RestoresInitialState()
    {
        var initial = _vm.IsWorkspacePanelVisible;
        _vm.ToggleWorkspacePanelCommand.Execute(null);
        _vm.ToggleWorkspacePanelCommand.Execute(null);
        Assert.AreEqual(initial, _vm.IsWorkspacePanelVisible);
    }

    // ── Autosave ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void EditorTextChange_SchedulesAutosave()
    {
        _vm.EditorText = "some content";
        Assert.IsTrue(_vm.IsDirty);
        Assert.IsTrue(_autosaveCoordinator.Calls.Count > 0);
    }

    [TestMethod]
    public void EditorText_WhenDirty_IsDirtyIsTrue()
    {
        _vm.EditorText = "modified";
        Assert.IsTrue(_vm.IsDirty);
    }

    // ── Commands CanExecute ────────────────────────────────────────────────────

    [TestMethod]
    public void SaveCommand_CanExecuteWhenNotBusy()
    {
        Assert.IsTrue(_vm.SaveCommand.CanExecute(null));
    }

    [TestMethod]
    public void ExportPdfCommand_CannotExecuteWhenNoParsedBlocks()
    {
        Assert.IsFalse(_vm.ExportPdfCommand.CanExecute(null));
    }

    [TestMethod]
    public void CreateMdsCommand_CannotExecuteWithoutWorkspace()
    {
        Assert.IsFalse(_vm.CreateMdsCommand.CanExecute(null));
    }

    // ── Preferences ────────────────────────────────────────────────────────────

    [TestMethod]
    public void Preferences_IsNotNull()
    {
        Assert.IsNotNull(_vm.Preferences);
    }

    [TestMethod]
    public void Preferences_AutoSaveEnabled_DefaultIsTrue()
    {
        Assert.IsTrue(_vm.Preferences.PreferencesAutoSaveEnabled);
    }

    // ── Recording sub-VM ───────────────────────────────────────────────────────

    [TestMethod]
    public void Recording_IsNotNull()
    {
        Assert.IsNotNull(_vm.Recording);
    }

    [TestMethod]
    public void Recording_IsNotRecordingInitially()
    {
        Assert.IsFalse(_vm.Recording.IsRecording);
    }

    // ── TranscriptionQueue sub-VM ──────────────────────────────────────────────

    [TestMethod]
    public void TranscriptionQueue_IsNotNull()
    {
        Assert.IsNotNull(_vm.TranscriptionQueue);
    }

    // ── Autosave scheduling ────────────────────────────────────────────────────

    [TestMethod]
    public void EditorText_WhenChanged_SchedulesAutosaveWithCurrentDirtyState()
    {
        _vm.EditorText = "something";
        var lastCall = _autosaveCoordinator.Calls.LastOrDefault();
        Assert.IsNotNull(lastCall);
        Assert.IsTrue(lastCall.IsDirty);
        Assert.IsTrue(lastCall.IsUntitled);
    }

    // ── KeyboardShortcutsChanged ───────────────────────────────────────────────

    [TestMethod]
    public void Preferences_PreferencesSaved_RaisesKeyboardShortcutsChanged()
    {
        var fired = false;
        _vm.KeyboardShortcutsChanged += (_, _) => fired = true;

        // Simulate saving preferences (which fires PreferencesSaved)
        // We trigger it by directly accessing the event
        _preferences.ShowPreferencesCommand.Execute(null);
        _preferences.SavePreferencesCommand.Execute(null);

        Assert.IsTrue(fired);
    }
}

// ── Additional fake needed for MainViewModel ───────────────────────────────

internal sealed class FakeDocumentWorkflowService : IDocumentWorkflowService
{
    public EditorDocumentState CreateDocumentState(MarkdownDocument document) =>
        new()
        {
            FilePath = document.FilePath,
            FileName = document.FileName ?? Path.GetFileName(document.FilePath),
            Content = document.Content,
            OriginalContent = document.Content,
            IsUntitled = document.IsUntitled
        };

    public DocumentPreviewResult PreparePreview(MarkdownDocument document, EditorViewMode currentViewMode) =>
        new() { Blocks = [], ViewMode = currentViewMode };

    public DocumentLoadResult PrepareDocument(MarkdownDocument document, EditorViewMode currentViewMode) =>
        new()
        {
            DocumentState = CreateDocumentState(document),
            Blocks = [],
            ViewMode = currentViewMode
        };

    public void ApplySaveResult(EditorDocumentState document, SaveDocumentResult result)
    {
        document.FilePath = result.FilePath;
        document.FileName = result.FileName;
        document.FileSizeBytes = result.FileSizeBytes;
        document.LastModified = result.LastModified;
        document.IsDirty = false;
        document.OriginalContent = document.Content;
    }
}

internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public DateTimeOffset Now => UtcNow.ToLocalTime();
}
