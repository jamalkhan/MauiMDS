using MauiMds.Core.Tests.TestHelpers;
using MauiMds.Features.Editor;

namespace MauiMds.Core.Tests.Features.Editor;

[TestClass]
public sealed class AutosaveCoordinatorTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(30);
    private static readonly string SomePath = "/workspace/doc.mds";

    private FakeClock _clock = null!;
    private FakeDelayScheduler _fakeDelay = null!;
    private FakeSynchronousDispatcher _dispatcher = null!;
    private AutosaveCoordinator _coordinator = null!;

    [TestInitialize]
    public void Setup()
    {
        _clock = new FakeClock();
        _fakeDelay = new FakeDelayScheduler(_clock);
        _dispatcher = new FakeSynchronousDispatcher();
        _coordinator = new AutosaveCoordinator(_fakeDelay, _dispatcher);
    }

    [TestCleanup]
    public void Cleanup() => _coordinator.Dispose();

    [TestMethod]
    public async Task Schedule_WhenEnabled_InvokesSaveActionAfterDelay()
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _coordinator.Schedule(
            isEnabled: true, isUntitled: false, isDirty: true,
            filePath: SomePath, delay: Delay,
            saveAction: () => { tcs.SetResult(true); return Task.CompletedTask; });

        // Let Task.Run start and register the delay before we advance
        await Task.Delay(20);

        _fakeDelay.AdvanceBy(Delay);

        var fired = await Task.WhenAny(tcs.Task, Task.Delay(500)) == tcs.Task;
        Assert.IsTrue(fired, "save action should fire within 500ms of delay completing");
    }

    [TestMethod]
    public async Task Schedule_WhenDisabled_DoesNotFire()
    {
        var callCount = 0;
        _coordinator.Schedule(
            isEnabled: false, isUntitled: false, isDirty: true,
            filePath: SomePath, delay: Delay,
            saveAction: () => { callCount++; return Task.CompletedTask; });

        _fakeDelay.AdvanceBy(Delay);
        await Task.Delay(50);

        Assert.AreEqual(0, callCount);
    }

    [TestMethod]
    public async Task Schedule_WhenUntitled_DoesNotFire()
    {
        var callCount = 0;
        _coordinator.Schedule(
            isEnabled: true, isUntitled: true, isDirty: true,
            filePath: SomePath, delay: Delay,
            saveAction: () => { callCount++; return Task.CompletedTask; });

        _fakeDelay.AdvanceBy(Delay);
        await Task.Delay(50);

        Assert.AreEqual(0, callCount);
    }

    [TestMethod]
    public async Task Schedule_WhenNotDirty_DoesNotFire()
    {
        var callCount = 0;
        _coordinator.Schedule(
            isEnabled: true, isUntitled: false, isDirty: false,
            filePath: SomePath, delay: Delay,
            saveAction: () => { callCount++; return Task.CompletedTask; });

        _fakeDelay.AdvanceBy(Delay);
        await Task.Delay(50);

        Assert.AreEqual(0, callCount);
    }

    [TestMethod]
    public async Task Schedule_WhenFilePathEmpty_DoesNotFire()
    {
        var callCount = 0;
        _coordinator.Schedule(
            isEnabled: true, isUntitled: false, isDirty: true,
            filePath: string.Empty, delay: Delay,
            saveAction: () => { callCount++; return Task.CompletedTask; });

        _fakeDelay.AdvanceBy(Delay);
        await Task.Delay(50);

        Assert.AreEqual(0, callCount);
    }

    [TestMethod]
    public async Task Cancel_PreventsScheduledActionFromRunning()
    {
        var callCount = 0;
        _coordinator.Schedule(
            isEnabled: true, isUntitled: false, isDirty: true,
            filePath: SomePath, delay: Delay,
            saveAction: () => { callCount++; return Task.CompletedTask; });

        _coordinator.Cancel();

        _fakeDelay.AdvanceBy(Delay);
        await Task.Delay(50);

        Assert.AreEqual(0, callCount, "cancelled action must not fire");
    }

    [TestMethod]
    public async Task Schedule_CalledTwice_OnlyLatestRuns()
    {
        var firstCount = 0;
        var secondTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _coordinator.Schedule(
            isEnabled: true, isUntitled: false, isDirty: true,
            filePath: SomePath, delay: Delay,
            saveAction: () => { firstCount++; return Task.CompletedTask; });

        await Task.Delay(20); // let first Task.Run start

        // Second schedule cancels the first
        _coordinator.Schedule(
            isEnabled: true, isUntitled: false, isDirty: true,
            filePath: SomePath, delay: Delay,
            saveAction: () => { secondTcs.SetResult(true); return Task.CompletedTask; });

        await Task.Delay(20); // let second Task.Run start

        _fakeDelay.AdvanceBy(Delay);

        var fired = await Task.WhenAny(secondTcs.Task, Task.Delay(500)) == secondTcs.Task;
        Assert.IsTrue(fired, "second schedule must run");
        Assert.AreEqual(0, firstCount, "first schedule must be cancelled");
    }

    [TestMethod]
    public async Task Dispose_CancelsScheduledAction()
    {
        var callCount = 0;
        _coordinator.Schedule(
            isEnabled: true, isUntitled: false, isDirty: true,
            filePath: SomePath, delay: Delay,
            saveAction: () => { callCount++; return Task.CompletedTask; });

        _coordinator.Dispose();

        _fakeDelay.AdvanceBy(Delay);
        await Task.Delay(50);

        Assert.AreEqual(0, callCount, "disposed coordinator must not invoke save");
    }

    [TestMethod]
    public void Cancel_WhenNothingScheduled_DoesNotThrow()
    {
        _coordinator.Cancel(); // should be idempotent
        _coordinator.Cancel();
    }

    [TestMethod]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        _coordinator.Dispose();
        _coordinator.Dispose();
    }
}
