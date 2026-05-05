using MauiMds.Features.Editor;
using MauiMds.Models;
using MauiMds.Processors;
using MauiMds.Core.Tests.TestHelpers;

namespace MauiMds.Core.Tests.Features.Editor;

[TestClass]
public sealed class PreviewPipelineControllerTests
{
    private static PreviewPipelineController CreateController(FakeClock clock, FakeDelayScheduler delayScheduler) =>
        new(new DocumentWorkflowController(
                new MdsParser(new TestLogger<MdsParser>()),
                new TestLogger<DocumentWorkflowController>()),
            delayScheduler,
            clock,
            new TestLogger<PreviewPipelineController>());

    private static (FakeClock clock, FakeDelayScheduler scheduler, PreviewPipelineController controller) Build()
    {
        var clock = new FakeClock();
        var scheduler = new FakeDelayScheduler(clock);
        return (clock, scheduler, CreateController(clock, scheduler));
    }

    [TestMethod]
    public async Task SchedulePreviewAsync_DebouncesAndAppliesOnlyLatestSnapshot()
    {
        var (_, delayScheduler, controller) = Build();
        var appliedFileNames = new List<string>();

        var firstTask = controller.SchedulePreviewAsync(
            new MarkdownDocument { FilePath = "/tmp/first.mds", FileName = "first.mds", Content = "# First" },
            EditorViewMode.Viewer,
            TimeSpan.FromMilliseconds(250),
            (snapshot, _, _) => { appliedFileNames.Add(snapshot.FileName ?? string.Empty); return Task.CompletedTask; });

        var secondTask = controller.SchedulePreviewAsync(
            new MarkdownDocument { FilePath = "/tmp/second.mds", FileName = "second.mds", Content = "# Second" },
            EditorViewMode.Viewer,
            TimeSpan.FromMilliseconds(250),
            (snapshot, _, _) => { appliedFileNames.Add(snapshot.FileName ?? string.Empty); return Task.CompletedTask; });

        delayScheduler.AdvanceBy(TimeSpan.FromMilliseconds(249));
        Assert.AreEqual(0, appliedFileNames.Count);

        delayScheduler.AdvanceBy(TimeSpan.FromMilliseconds(1));
        await Task.WhenAll(firstTask, secondTask);

        CollectionAssert.AreEqual(new[] { "second.mds" }, appliedFileNames);
    }

    [TestMethod]
    public async Task ScheduleExternalReloadAsync_DebouncesAndRunsOnlyLatestReload()
    {
        var (_, delayScheduler, controller) = Build();
        var reloadCount = 0;

        var firstTask = controller.ScheduleExternalReloadAsync(
            TimeSpan.FromMilliseconds(400),
            () => { reloadCount++; return Task.CompletedTask; });

        var secondTask = controller.ScheduleExternalReloadAsync(
            TimeSpan.FromMilliseconds(400),
            () => { reloadCount++; return Task.CompletedTask; });

        delayScheduler.AdvanceBy(TimeSpan.FromMilliseconds(399));
        Assert.AreEqual(0, reloadCount);

        delayScheduler.AdvanceBy(TimeSpan.FromMilliseconds(1));
        await Task.WhenAll(firstTask, secondTask);

        Assert.AreEqual(1, reloadCount);
    }

    // ── New tests ────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task SchedulePreviewAsync_SingleCall_RunsExactlyOnceAfterDelay()
    {
        var (_, delayScheduler, controller) = Build();
        var callCount = 0;

        var task = controller.SchedulePreviewAsync(
            new MarkdownDocument { FilePath = "/tmp/doc.mds", FileName = "doc.mds", Content = "# Title" },
            EditorViewMode.Viewer,
            TimeSpan.FromMilliseconds(100),
            (_, _, _) => { callCount++; return Task.CompletedTask; });

        Assert.AreEqual(0, callCount, "Should not fire before delay");

        delayScheduler.AdvanceBy(TimeSpan.FromMilliseconds(100));
        await task;

        Assert.AreEqual(1, callCount, "Should fire exactly once after delay");
    }

    [TestMethod]
    public async Task SchedulePreviewAsync_CancelledBeforeDeadline_DoesNotFire()
    {
        var (_, delayScheduler, controller) = Build();
        var fired = false;

        var task = controller.SchedulePreviewAsync(
            new MarkdownDocument { FilePath = "/tmp/doc.mds", FileName = "doc.mds", Content = "# Title" },
            EditorViewMode.Viewer,
            TimeSpan.FromMilliseconds(200),
            (_, _, _) => { fired = true; return Task.CompletedTask; });

        controller.CancelPreview();

        delayScheduler.AdvanceBy(TimeSpan.FromMilliseconds(200));
        await task;

        Assert.IsFalse(fired, "Preview should not fire after cancellation");
    }

    [TestMethod]
    public void ShouldSuppressExternalReload_WithinSuppressionWindow_ReturnsTrue()
    {
        var (clock, _, controller) = Build();
        controller.MarkSaved();

        clock.Advance(TimeSpan.FromMilliseconds(500));

        var suppress = controller.ShouldSuppressExternalReload(isSavingDocument: false, suppressionWindow: TimeSpan.FromSeconds(2));

        Assert.IsTrue(suppress, "Should suppress reload within suppression window after save");
    }

    [TestMethod]
    public void ShouldSuppressExternalReload_OutsideSuppressionWindow_ReturnsFalse()
    {
        var (clock, _, controller) = Build();
        controller.MarkSaved();

        clock.Advance(TimeSpan.FromSeconds(5));

        var suppress = controller.ShouldSuppressExternalReload(isSavingDocument: false, suppressionWindow: TimeSpan.FromSeconds(2));

        Assert.IsFalse(suppress, "Should not suppress reload after suppression window has passed");
    }

    [TestMethod]
    public void Dispose_CanBeCalledSafely()
    {
        var (_, _, controller) = Build();
        controller.Dispose();
        // Second dispose should also be safe
        controller.Dispose();
    }

    [TestMethod]
    public void ShouldSuppressExternalReload_WhenIsSavingDocument_AlwaysReturnsTrue()
    {
        var (clock, _, controller) = Build();

        clock.Advance(TimeSpan.FromSeconds(100));

        var suppress = controller.ShouldSuppressExternalReload(isSavingDocument: true, suppressionWindow: TimeSpan.FromSeconds(1));

        Assert.IsTrue(suppress, "isSavingDocument=true should always suppress");
    }
}
