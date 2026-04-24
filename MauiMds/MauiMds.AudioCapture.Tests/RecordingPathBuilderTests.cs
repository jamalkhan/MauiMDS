namespace MauiMds.AudioCapture.Tests;

[TestClass]
public sealed class RecordingPathBuilderTests
{
    private static readonly DateTimeOffset FixedTime = new(2026, 4, 22, 15, 30, 45, TimeSpan.Zero);

    [TestMethod]
    public void Build_PlacesFileInRecordingsSubfolder()
    {
        var path = RecordingPathBuilder.Build("/Users/user/Documents/workspace", FixedTime);
        var dir = Path.GetDirectoryName(path)!;
        Assert.AreEqual("Recordings", Path.GetFileName(dir));
    }

    [TestMethod]
    public void Build_FileNameStartsWithAudioCapture()
    {
        var path = RecordingPathBuilder.Build("/base", FixedTime);
        StringAssert.StartsWith(Path.GetFileName(path), "audio_capture_");
    }

    [TestMethod]
    public void Build_FileNameHasM4aExtension()
    {
        var path = RecordingPathBuilder.Build("/base", FixedTime);
        StringAssert.EndsWith(path, ".m4a");
    }

    [TestMethod]
    public void Build_FileNameContainsDateInExpectedFormat()
    {
        var path = RecordingPathBuilder.Build("/base", FixedTime);
        StringAssert.Contains(path, "2026_04_22");
    }

    [TestMethod]
    public void Build_FileNameContainsTimeInExpectedFormat()
    {
        var path = RecordingPathBuilder.Build("/base", FixedTime);
        StringAssert.Contains(path, "153045");
    }

    [TestMethod]
    public void Build_FullFileNameMatchesExpectedPattern()
    {
        var path = RecordingPathBuilder.Build("/base", FixedTime);
        Assert.AreEqual("audio_capture_2026_04_22_153045.m4a", Path.GetFileName(path));
    }

    [TestMethod]
    public void Build_CombinesBaseFolderWithRecordingsSubdir()
    {
        const string baseFolder = "/Users/user/workspace";
        var path = RecordingPathBuilder.Build(baseFolder, FixedTime);
        StringAssert.StartsWith(path, baseFolder);
    }

    [TestMethod]
    public void Build_DifferentTimestampsProduceDifferentPaths()
    {
        var time1 = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var time2 = new DateTimeOffset(2026, 1, 1, 10, 0, 1, TimeSpan.Zero);
        var path1 = RecordingPathBuilder.Build("/base", time1);
        var path2 = RecordingPathBuilder.Build("/base", time2);
        Assert.AreNotEqual(path1, path2);
    }

    [TestMethod]
    public void Build_SameTimestampAndBaseFolderProduceSamePath()
    {
        var path1 = RecordingPathBuilder.Build("/base", FixedTime);
        var path2 = RecordingPathBuilder.Build("/base", FixedTime);
        Assert.AreEqual(path1, path2);
    }

    [TestMethod]
    public void Build_RecordingsFolderNameConstantMatchesActualFolder()
    {
        var path = RecordingPathBuilder.Build("/base", FixedTime);
        StringAssert.Contains(path, RecordingPathBuilder.RecordingsFolderName);
    }

    [TestMethod]
    public void Build_NestedBaseFolderPathIsPreserved()
    {
        const string deep = "/a/b/c/d";
        var path = RecordingPathBuilder.Build(deep, FixedTime);
        StringAssert.StartsWith(path, deep);
    }
}
