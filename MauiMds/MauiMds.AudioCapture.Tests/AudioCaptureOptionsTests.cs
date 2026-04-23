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
    public void Default_CaptureSystemAudio_IsTrue()
    {
        var options = new AudioCaptureOptions();
        Assert.IsTrue(options.CaptureSystemAudio);
    }

    [TestMethod]
    public void Default_CaptureMicrophone_IsTrue()
    {
        var options = new AudioCaptureOptions();
        Assert.IsTrue(options.CaptureMicrophone);
    }

    [TestMethod]
    public void Default_OutputPath_IsEmpty()
    {
        var options = new AudioCaptureOptions();
        Assert.AreEqual(string.Empty, options.OutputPath);
    }

    [TestMethod]
    public void WithInit_AllPropertiesCanBeSet()
    {
        var options = new AudioCaptureOptions
        {
            OutputPath = "/path/to/output.m4a",
            SampleRate = 44_100,
            ChannelCount = 1,
            EncoderBitRate = 64_000,
            CaptureSystemAudio = false,
            CaptureMicrophone = false
        };

        Assert.AreEqual("/path/to/output.m4a", options.OutputPath);
        Assert.AreEqual(44_100, options.SampleRate);
        Assert.AreEqual(1, options.ChannelCount);
        Assert.AreEqual(64_000, options.EncoderBitRate);
        Assert.IsFalse(options.CaptureSystemAudio);
        Assert.IsFalse(options.CaptureMicrophone);
    }
}
