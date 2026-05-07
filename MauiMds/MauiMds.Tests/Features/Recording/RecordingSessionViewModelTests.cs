using MauiMds.AudioCapture;
using MauiMds.Models;
using MauiMds.Tests.TestHelpers;
using MauiMds.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace MauiMds.Tests.Features.Recording;

[TestClass]
public sealed class RecordingSessionViewModelTests
{
    private FakeAudioCaptureService _capture = null!;
    private FakeAudioPlayerService _player = null!;
    private FakeApplicationLifetime _lifetime = null!;
    private List<(string Title, Exception? Ex, string Msg)> _errors = null!;
    private RecordingSessionViewModel _vm = null!;

    [TestInitialize]
    public void Setup()
    {
        _capture = new FakeAudioCaptureService();
        _player = new FakeAudioPlayerService();
        _lifetime = new FakeApplicationLifetime();
        _errors = [];

        _vm = new RecordingSessionViewModel(
            _capture,
            _player,
            new FakeClock(),
            NullLogger<RecordingSessionViewModel>.Instance,
            new FakeSynchronousDispatcher(),
            _lifetime,
            () => RecordingFormat.M4A,
            () => 30,
            () => Path.GetTempPath(),
            (title, ex, msg) => { _errors.Add((title, ex, msg)); return Task.CompletedTask; });
    }

    // ── Initial state ─────────────────────────────────────────────────────────

    [TestMethod]
    public void InitialState_IsRecordingFalse()
        => Assert.IsFalse(_vm.IsRecording);

    [TestMethod]
    public void InitialState_RecordButtonLabelIsRecord()
        => Assert.AreEqual("Record", _vm.RecordButtonLabel);

    [TestMethod]
    public void InitialState_IsRecordingGroupSelectedFalse()
        => Assert.IsFalse(_vm.IsRecordingGroupSelected);

    [TestMethod]
    public void InitialState_PlaybackPositionIsZero()
        => Assert.AreEqual(TimeSpan.Zero, _vm.PlaybackPosition);

    [TestMethod]
    public void InitialState_CurrentlyPlayingAudioPathIsNull()
        => Assert.IsNull(_vm.CurrentlyPlayingAudioPath);

    // ── SelectedRecordingGroup ────────────────────────────────────────────────

    [TestMethod]
    public void SelectedRecordingGroup_Set_FiresPropertyChanged()
    {
        var fired = new List<string?>();
        _vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        _vm.SelectedRecordingGroup = new RecordingGroup { BaseName = "x", DirectoryPath = Path.GetTempPath() };

        CollectionAssert.Contains(fired, nameof(_vm.SelectedRecordingGroup));
        CollectionAssert.Contains(fired, nameof(_vm.IsRecordingGroupSelected));
    }

    [TestMethod]
    public void SelectedRecordingGroup_Set_IsRecordingGroupSelectedBecomesTrue()
    {
        _vm.SelectedRecordingGroup = new RecordingGroup { BaseName = "x", DirectoryPath = Path.GetTempPath() };
        Assert.IsTrue(_vm.IsRecordingGroupSelected);
    }

    [TestMethod]
    public void SelectedRecordingGroup_SetSameValue_DoesNotFirePropertyChanged()
    {
        var group = new RecordingGroup { BaseName = "x", DirectoryPath = Path.GetTempPath() };
        _vm.SelectedRecordingGroup = group;

        var fired = new List<string?>();
        _vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        _vm.SelectedRecordingGroup = group;
        Assert.AreEqual(0, fired.Count);
    }

    // ── Permission: Denied ────────────────────────────────────────────────────

    [TestMethod]
    public void ToggleRecording_MicPermissionDenied_ReportsErrorAndDoesNotRecord()
    {
        _capture.PermissionToReturn = AudioPermissionStatus.Denied;

        _vm.ToggleRecordingCommand.Execute(null);

        Assert.AreEqual(1, _errors.Count);
        StringAssert.Contains(_errors[0].Title.ToLower(), "denied");
        Assert.IsFalse(_vm.IsRecording);
    }

    // ── Permission: NotDetermined → request → denied ──────────────────────────

    [TestMethod]
    public void ToggleRecording_PermissionNotDetermined_RequestDenied_ReportsError()
    {
        _capture.PermissionToReturn = AudioPermissionStatus.NotDetermined;
        _capture.RequestPermissionToReturn = AudioPermissionStatus.Denied;

        _vm.ToggleRecordingCommand.Execute(null);

        Assert.AreEqual(1, _errors.Count);
        Assert.IsFalse(_vm.IsRecording);
    }

    // ── Permission: NotDetermined → request → granted ─────────────────────────

    [TestMethod]
    public void ToggleRecording_PermissionNotDetermined_RequestGranted_StartsRecording()
    {
        _capture.PermissionToReturn = AudioPermissionStatus.NotDetermined;
        _capture.RequestPermissionToReturn = AudioPermissionStatus.Granted;

        _vm.ToggleRecordingCommand.Execute(null);

        Assert.IsTrue(_vm.IsRecording);
        Assert.AreEqual(0, _errors.Count);
    }

    // ── Successful start ──────────────────────────────────────────────────────

    [TestMethod]
    public void ToggleRecording_Start_IsRecordingBecomesTrue()
    {
        _vm.ToggleRecordingCommand.Execute(null);
        Assert.IsTrue(_vm.IsRecording);
    }

    [TestMethod]
    public void ToggleRecording_Start_RecordButtonLabelChanges()
    {
        _vm.ToggleRecordingCommand.Execute(null);
        Assert.AreEqual("Stop Recording...", _vm.RecordButtonLabel);
    }

    [TestMethod]
    public void ToggleRecording_Start_FiresRecordingStartedEvent()
    {
        RecordingGroup? startedGroup = null;
        _vm.RecordingStarted += (_, g) => startedGroup = g;

        _vm.ToggleRecordingCommand.Execute(null);

        Assert.IsNotNull(startedGroup);
    }

    [TestMethod]
    public void ToggleRecording_Start_NoErrors()
    {
        _vm.ToggleRecordingCommand.Execute(null);
        Assert.AreEqual(0, _errors.Count);
    }

    // ── ScreenRecordingDenied warning ─────────────────────────────────────────

    [TestMethod]
    public void ToggleRecording_ScreenRecordingDeniedWarning_ReportsErrorButRecordingIsActive()
    {
        _capture.LastStartWarning = AudioCaptureWarnings.ScreenRecordingDenied;

        _vm.ToggleRecordingCommand.Execute(null);

        Assert.AreEqual(1, _errors.Count);
        StringAssert.Contains(_errors[0].Title.ToLower(), "system audio");
        Assert.IsTrue(_vm.IsRecording);
    }

    // ── WasapiLoopbackUnavailable warning ─────────────────────────────────────

    [TestMethod]
    public void ToggleRecording_WasapiLoopbackUnavailableWarning_ReportsErrorButRecordingIsActive()
    {
        _capture.LastStartWarning = AudioCaptureWarnings.WasapiLoopbackUnavailable;

        _vm.ToggleRecordingCommand.Execute(null);

        Assert.AreEqual(1, _errors.Count);
        StringAssert.Contains(_errors[0].Title.ToLower(), "system audio");
        Assert.IsTrue(_vm.IsRecording);
    }

    // ── Stop recording ────────────────────────────────────────────────────────

    [TestMethod]
    public void ToggleRecording_Stop_IsRecordingBecomesFalse()
    {
        _vm.ToggleRecordingCommand.Execute(null);  // start
        _vm.ToggleRecordingCommand.Execute(null);  // stop

        Assert.IsFalse(_vm.IsRecording);
    }

    [TestMethod]
    public void ToggleRecording_Stop_RecordButtonLabelResets()
    {
        _vm.ToggleRecordingCommand.Execute(null);
        _vm.ToggleRecordingCommand.Execute(null);

        Assert.AreEqual("Record", _vm.RecordButtonLabel);
    }

    [TestMethod]
    public void ToggleRecording_Stop_FiresRecordingStoppedEvent()
    {
        RecordingStoppedEventArgs? stoppedArgs = null;
        _vm.RecordingStopped += (_, e) => stoppedArgs = e;

        _vm.ToggleRecordingCommand.Execute(null);
        _vm.ToggleRecordingCommand.Execute(null);

        Assert.IsNotNull(stoppedArgs);
        Assert.IsTrue(stoppedArgs.Result.Success);
    }

    [TestMethod]
    public void ToggleRecording_StopFails_ReportsError()
    {
        _capture.ResultToReturn = new AudioCaptureResult { Success = false, ErrorMessage = "disk full" };

        _vm.ToggleRecordingCommand.Execute(null);  // start
        _vm.ToggleRecordingCommand.Execute(null);  // stop

        Assert.AreEqual(1, _errors.Count);
    }

    [TestMethod]
    public void ToggleRecording_StopFails_RecordingStoppedEventNotFired()
    {
        _capture.ResultToReturn = new AudioCaptureResult { Success = false, ErrorMessage = "disk full" };
        var fired = false;
        _vm.RecordingStopped += (_, _) => fired = true;

        _vm.ToggleRecordingCommand.Execute(null);
        _vm.ToggleRecordingCommand.Execute(null);

        Assert.IsFalse(fired);
    }

    // ── Playback delegation ───────────────────────────────────────────────────

    [TestMethod]
    public void PlaybackPosition_ReflectsPlayerService()
    {
        _player.Position = TimeSpan.FromSeconds(42);
        Assert.AreEqual(TimeSpan.FromSeconds(42), _vm.PlaybackPosition);
    }

    [TestMethod]
    public void PlaybackDuration_ReflectsPlayerService()
    {
        _player.Duration = TimeSpan.FromMinutes(5);
        Assert.AreEqual(TimeSpan.FromMinutes(5), _vm.PlaybackDuration);
    }

    [TestMethod]
    public void CurrentlyPlayingAudioPath_AfterPlay_ReflectsPlayerService()
    {
        _player.Position = TimeSpan.FromSeconds(1);  // simulate playing state
        _vm.PlayAudioCommand.Execute("/test/file.m4a");

        Assert.AreEqual("/test/file.m4a", _vm.CurrentlyPlayingAudioPath);
    }

    // ── Rewind / FastForward ──────────────────────────────────────────────────

    [TestMethod]
    public void RewindCommand_Seeks15SecBack()
    {
        _player.Position = TimeSpan.FromSeconds(60);
        _player.Duration = TimeSpan.FromSeconds(120);

        _vm.RewindCommand.Execute(null);

        Assert.AreEqual(TimeSpan.FromSeconds(45), _player.Position);
    }

    [TestMethod]
    public void RewindCommand_ClampsToZeroWhenNearStart()
    {
        _player.Position = TimeSpan.FromSeconds(5);
        _player.Duration = TimeSpan.FromSeconds(120);

        _vm.RewindCommand.Execute(null);

        Assert.AreEqual(TimeSpan.Zero, _player.Position);
    }

    [TestMethod]
    public void FastForwardCommand_Seeks15SecForward()
    {
        _player.Position = TimeSpan.FromSeconds(60);
        _player.Duration = TimeSpan.FromSeconds(120);

        _vm.FastForwardCommand.Execute(null);

        Assert.AreEqual(TimeSpan.FromSeconds(75), _player.Position);
    }

    [TestMethod]
    public void FastForwardCommand_ClampsAtDuration()
    {
        _player.Position = TimeSpan.FromSeconds(115);
        _player.Duration = TimeSpan.FromSeconds(120);

        _vm.FastForwardCommand.Execute(null);

        Assert.AreEqual(TimeSpan.FromSeconds(120), _player.Position);
    }

    // ── SeekCommand ───────────────────────────────────────────────────────────

    [TestMethod]
    public void SeekCommand_SeeksToGivenSeconds()
    {
        _vm.SeekCommand.Execute((double)42.5);
        Assert.AreEqual(TimeSpan.FromSeconds(42.5), _player.Position);
    }

    [TestMethod]
    public void SeekCommand_NonDoubleParam_DoesNotCrash()
    {
        _vm.SeekCommand.Execute("not a double");
        Assert.AreEqual(TimeSpan.Zero, _player.Position);
    }

    // ── PropertyChanged from player events ────────────────────────────────────

    [TestMethod]
    public void PlaybackPositionChanged_FiresViewModelPropertyChanged()
    {
        var fired = new List<string?>();
        _vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        _player.Seek(TimeSpan.FromSeconds(10));

        CollectionAssert.Contains(fired, nameof(_vm.PlaybackPosition));
    }

    [TestMethod]
    public void PlaybackStateChanged_FiresCurrentlyPlayingAudioPathPropertyChanged()
    {
        var fired = new List<string?>();
        _vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        _vm.PlayAudioCommand.Execute("/test.m4a");

        CollectionAssert.Contains(fired, nameof(_vm.CurrentlyPlayingAudioPath));
    }

    // ── StartAsync throws ─────────────────────────────────────────────────────

    [TestMethod]
    public void ToggleRecording_StartThrows_ReportsError()
    {
        _capture.StartException = new InvalidOperationException("no audio device");

        _vm.ToggleRecordingCommand.Execute(null);

        Assert.AreEqual(1, _errors.Count);
        StringAssert.Contains(_errors[0].Title.ToLower(), "start");
    }

    [TestMethod]
    public void ToggleRecording_StartThrows_IsRecordingStaysFalse()
    {
        _capture.StartException = new InvalidOperationException("no audio device");

        _vm.ToggleRecordingCommand.Execute(null);

        Assert.IsFalse(_vm.IsRecording);
    }

    [TestMethod]
    public void ToggleRecording_StartThrows_CanRecordAgainAfterward()
    {
        _capture.StartException = new InvalidOperationException("transient failure");
        _vm.ToggleRecordingCommand.Execute(null);  // fails

        _capture.StartException = null;
        _vm.ToggleRecordingCommand.Execute(null);  // should succeed now

        Assert.IsTrue(_vm.IsRecording);
    }

    // ── IsRecording update is dispatched via main thread, not inline ──────────

    [TestMethod]
    public void StateChangeEvent_IsDispatchedToMainThread_NotInline()
    {
        var queuedDispatcher = new FakeQueuedDispatcher();
        var vm = CreateVm(queuedDispatcher);

        vm.ToggleRecordingCommand.Execute(null);

        // The StateChanged event fired, but BeginInvokeOnMainThread was only queued —
        // IsRecording must still be false until the dispatcher flushes.
        Assert.IsFalse(vm.IsRecording,
            "IsRecording should not update before the dispatcher flushes.");
        Assert.AreEqual(1, queuedDispatcher.QueuedCount,
            "One deferred action should be waiting in the dispatcher queue.");

        queuedDispatcher.Flush();

        Assert.IsTrue(vm.IsRecording,
            "IsRecording should be true after the dispatcher flushes.");
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private RecordingSessionViewModel CreateVm(IMainThreadDispatcher? dispatcher = null)
        => new(
            _capture,
            _player,
            new FakeClock(),
            NullLogger<RecordingSessionViewModel>.Instance,
            dispatcher ?? new FakeSynchronousDispatcher(),
            _lifetime,
            () => RecordingFormat.M4A,
            () => 30,
            () => Path.GetTempPath(),
            (title, ex, msg) => { _errors.Add((title, ex, msg)); return Task.CompletedTask; });
}
