using MauiMds.Features.Editor;
using MauiMds.Models;
using MauiMds.Processors;
using MauiMds.Tests.TestHelpers;

namespace MauiMds.Tests.Features.Editor;

[TestClass]
public sealed class PreviewPipelineControllerTests
{
    [TestMethod]
    public async Task SchedulePreviewAsync_DebouncesAndAppliesOnlyLatestSnapshot()
    {
        var clock = new FakeClock();
        var delayScheduler = new FakeDelayScheduler(clock);
        var controller = CreateController(clock, delayScheduler);
        var appliedFileNames = new List<string>();

        var firstTask = controller.SchedulePreviewAsync(
            new MarkdownDocument
            {
                FilePath = "/tmp/first.mds",
                FileName = "first.mds",
                Content = "# First"
            },
            EditorViewMode.Viewer,
            TimeSpan.FromMilliseconds(250),
            (snapshot, _, _) =>
            {
                appliedFileNames.Add(snapshot.FileName ?? string.Empty);
                return Task.CompletedTask;
            });

        var secondTask = controller.SchedulePreviewAsync(
            new MarkdownDocument
            {
                FilePath = "/tmp/second.mds",
                FileName = "second.mds",
                Content = "# Second"
            },
            EditorViewMode.Viewer,
            TimeSpan.FromMilliseconds(250),
            (snapshot, _, _) =>
            {
                appliedFileNames.Add(snapshot.FileName ?? string.Empty);
                return Task.CompletedTask;
            });

        delayScheduler.AdvanceBy(TimeSpan.FromMilliseconds(249));
        Assert.AreEqual(0, appliedFileNames.Count);

        delayScheduler.AdvanceBy(TimeSpan.FromMilliseconds(1));
        await Task.WhenAll(firstTask, secondTask);

        CollectionAssert.AreEqual(new[] { "second.mds" }, appliedFileNames);
    }

    [TestMethod]
    public async Task ScheduleExternalReloadAsync_DebouncesAndRunsOnlyLatestReload()
    {
        var clock = new FakeClock();
        var delayScheduler = new FakeDelayScheduler(clock);
        var controller = CreateController(clock, delayScheduler);
        var reloadCount = 0;

        var firstTask = controller.ScheduleExternalReloadAsync(
            TimeSpan.FromMilliseconds(400),
            () =>
            {
                reloadCount++;
                return Task.CompletedTask;
            });

        var secondTask = controller.ScheduleExternalReloadAsync(
            TimeSpan.FromMilliseconds(400),
            () =>
            {
                reloadCount++;
                return Task.CompletedTask;
            });

        delayScheduler.AdvanceBy(TimeSpan.FromMilliseconds(399));
        Assert.AreEqual(0, reloadCount);

        delayScheduler.AdvanceBy(TimeSpan.FromMilliseconds(1));
        await Task.WhenAll(firstTask, secondTask);

        Assert.AreEqual(1, reloadCount);
    }

    private static PreviewPipelineController CreateController(FakeClock clock, FakeDelayScheduler delayScheduler)
    {
        return new PreviewPipelineController(
            new DocumentWorkflowController(
                new MdsParser(new TestLogger<MdsParser>()),
                new TestLogger<DocumentWorkflowController>()),
            delayScheduler,
            clock,
            new TestLogger<PreviewPipelineController>());
    }
}
