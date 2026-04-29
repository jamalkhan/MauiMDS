namespace MauiMds.AudioCapture.Tests;

[TestClass]
public sealed class AudioCaptureOptionsTests
{
    [TestMethod]
    public void Default_SampleRate_Is48000()
    {
        var options = new AudioCaptureOptions();
        Assert.AreEqual(48_000, options.SampleRate);
    }

    [TestMethod]
    public void Default_ChannelCount_Is2()
    {
        var options = new AudioCaptureOptions();
        Assert.AreEqual(2, options.ChannelCount);
    }

    [TestMethod]
    public void Default_EncoderBitRate_Is128000()
    {
        var options = new AudioCaptureOptions();
        Assert.AreEqual(128_000, options.EncoderBitRate);
    }

    [TestMethod]
    public void Default_CaptureSystemAudio_IsFalse_WhenNoSysPath()
    {
        var options = new AudioCaptureOptions();
        Assert.IsFalse(options.CaptureSystemAudio);
    }

    [TestMethod]
    public void CaptureSystemAudio_IsTrue_WhenSysPathProvided()
    {
        var options = new AudioCaptureOptions { SysOutputPath = "/path/to/sys.m4a" };
        Assert.IsTrue(options.CaptureSystemAudio);
    }

    [TestMethod]
    public void Default_CaptureMicrophone_IsFalse_WhenNoOutputPath()
    {
        var options = new AudioCaptureOptions();
        Assert.IsFalse(options.CaptureMicrophone);
    }

    [TestMethod]
    public void CaptureMicrophone_IsTrue_WhenOutputPathProvided()
    {
        var options = new AudioCaptureOptions { OutputPath = "/path/to/mic.m4a" };
        Assert.IsTrue(options.CaptureMicrophone);
    }

    [TestMethod]
    public void Default_OutputPath_IsEmpty()
    {
        var options = new AudioCaptureOptions();
        Assert.AreEqual(string.Empty, options.OutputPath);
    }

    [TestMethod]
    public void Default_SysOutputPath_IsEmpty()
    {
        var options = new AudioCaptureOptions();
        Assert.AreEqual(string.Empty, options.SysOutputPath);
    }

    [TestMethod]
    public void WithInit_AllPropertiesCanBeSet()
    {
        var options = new AudioCaptureOptions
        {
            OutputPath = "/path/to/mic.m4a",
            SysOutputPath = "/path/to/sys.m4a",
            SampleRate = 44_100,
            ChannelCount = 1,
            EncoderBitRate = 64_000,
        };

        Assert.AreEqual("/path/to/mic.m4a", options.OutputPath);
        Assert.AreEqual("/path/to/sys.m4a", options.SysOutputPath);
        Assert.AreEqual(44_100, options.SampleRate);
        Assert.AreEqual(1, options.ChannelCount);
        Assert.AreEqual(64_000, options.EncoderBitRate);
        Assert.IsTrue(options.CaptureSystemAudio);
        Assert.IsTrue(options.CaptureMicrophone);
    }
}
