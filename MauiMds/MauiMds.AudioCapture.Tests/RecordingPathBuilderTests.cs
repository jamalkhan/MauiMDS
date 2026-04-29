namespace MauiMds.AudioCapture.Tests;

[TestClass]
public sealed class RecordingPathBuilderTests
{
    private static readonly DateTimeOffset FixedTime = new(2026, 4, 22, 15, 30, 45, TimeSpan.Zero);

    // ── Build (legacy) ────────────────────────────────────────────────────

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

    // ── BuildMic ──────────────────────────────────────────────────────────

    [TestMethod]
    public void BuildMic_FileNameHasMicSuffix()
    {
        var path = RecordingPathBuilder.BuildMic("/base", FixedTime);
        StringAssert.Contains(Path.GetFileNameWithoutExtension(path), "_mic");
    }

    [TestMethod]
    public void BuildMic_DefaultExtensionIsM4a()
    {
        var path = RecordingPathBuilder.BuildMic("/base", FixedTime);
        StringAssert.EndsWith(path, ".m4a");
    }

    [TestMethod]
    public void BuildMic_CustomExtensionIsApplied()
    {
        var path = RecordingPathBuilder.BuildMic("/base", FixedTime, ".mp3");
        StringAssert.EndsWith(path, ".mp3");
    }

    [TestMethod]
    public void BuildMic_FullFileNameMatchesExpectedPattern()
    {
        var path = RecordingPathBuilder.BuildMic("/base", FixedTime);
        Assert.AreEqual("audio_capture_2026_04_22_153045_mic.m4a", Path.GetFileName(path));
    }

    // ── BuildSys ──────────────────────────────────────────────────────────

    [TestMethod]
    public void BuildSys_FileNameHasSysSuffix()
    {
        var path = RecordingPathBuilder.BuildSys("/base", FixedTime);
        StringAssert.Contains(Path.GetFileNameWithoutExtension(path), "_sys");
    }

    [TestMethod]
    public void BuildSys_DefaultExtensionIsM4a()
    {
        var path = RecordingPathBuilder.BuildSys("/base", FixedTime);
        StringAssert.EndsWith(path, ".m4a");
    }

    [TestMethod]
    public void BuildSys_FullFileNameMatchesExpectedPattern()
    {
        var path = RecordingPathBuilder.BuildSys("/base", FixedTime);
        Assert.AreEqual("audio_capture_2026_04_22_153045_sys.m4a", Path.GetFileName(path));
    }

    // ── BuildTranscript ───────────────────────────────────────────────────

    [TestMethod]
    public void BuildTranscript_FileNameHasTranscriptSuffix()
    {
        var path = RecordingPathBuilder.BuildTranscript("/base", FixedTime);
        StringAssert.Contains(Path.GetFileNameWithoutExtension(path), "_transcript");
    }

    [TestMethod]
    public void BuildTranscript_ExtensionIsMds()
    {
        var path = RecordingPathBuilder.BuildTranscript("/base", FixedTime);
        StringAssert.EndsWith(path, ".mds");
    }

    [TestMethod]
    public void BuildTranscript_FullFileNameMatchesExpectedPattern()
    {
        var path = RecordingPathBuilder.BuildTranscript("/base", FixedTime);
        Assert.AreEqual("audio_capture_2026_04_22_153045_transcript.mds", Path.GetFileName(path));
    }

    // ── TryParseGroupFile ─────────────────────────────────────────────────

    [TestMethod]
    public void TryParseGroupFile_MicFile_ReturnsTrueWithCorrectParts()
    {
        var result = RecordingPathBuilder.TryParseGroupFile(
            "audio_capture_2026_04_22_153045_mic.m4a", out var baseName, out var role);
        Assert.IsTrue(result);
        Assert.AreEqual("audio_capture_2026_04_22_153045", baseName);
        Assert.AreEqual("mic", role);
    }

    [TestMethod]
    public void TryParseGroupFile_SysFile_ReturnsTrueWithCorrectParts()
    {
        var result = RecordingPathBuilder.TryParseGroupFile(
            "audio_capture_2026_04_22_153045_sys.m4a", out var baseName, out var role);
        Assert.IsTrue(result);
        Assert.AreEqual("audio_capture_2026_04_22_153045", baseName);
        Assert.AreEqual("sys", role);
    }

    [TestMethod]
    public void TryParseGroupFile_TranscriptFile_ReturnsTrueWithCorrectParts()
    {
        var result = RecordingPathBuilder.TryParseGroupFile(
            "audio_capture_2026_04_22_153045_transcript.mds", out var baseName, out var role);
        Assert.IsTrue(result);
        Assert.AreEqual("audio_capture_2026_04_22_153045", baseName);
        Assert.AreEqual("transcript", role);
    }

    [TestMethod]
    public void TryParseGroupFile_LegacyFile_ReturnsFalse()
    {
        var result = RecordingPathBuilder.TryParseGroupFile(
            "audio_capture_2026_04_22_153045.m4a", out var baseName, out var role);
        Assert.IsFalse(result);
        Assert.AreEqual(string.Empty, baseName);
        Assert.AreEqual(string.Empty, role);
    }

    [TestMethod]
    public void TryParseGroupFile_ArbitraryFile_ReturnsFalse()
    {
        var result = RecordingPathBuilder.TryParseGroupFile("notes.md", out _, out _);
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void TryParseGroupFile_IsCaseInsensitive()
    {
        var result = RecordingPathBuilder.TryParseGroupFile(
            "AUDIO_CAPTURE_2026_04_22_153045_MIC.M4A", out _, out var role);
        Assert.IsTrue(result);
        Assert.AreEqual("mic", role);
    }
}
