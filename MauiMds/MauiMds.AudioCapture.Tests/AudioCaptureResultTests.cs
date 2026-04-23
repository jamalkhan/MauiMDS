namespace MauiMds.AudioCapture.Tests;

[TestClass]
public sealed class AudioCaptureResultTests
{
    [TestMethod]
    public void Default_Success_IsFalse()
    {
        var result = new AudioCaptureResult();
        Assert.IsFalse(result.Success);
    }

    [TestMethod]
    public void Default_FilePath_IsEmpty()
    {
        var result = new AudioCaptureResult();
        Assert.AreEqual(string.Empty, result.FilePath);
    }

    [TestMethod]
    public void Default_ErrorMessage_IsNull()
    {
        var result = new AudioCaptureResult();
        Assert.IsNull(result.ErrorMessage);
    }

    [TestMethod]
    public void Default_Duration_IsZero()
    {
        var result = new AudioCaptureResult();
        Assert.AreEqual(TimeSpan.Zero, result.Duration);
    }

    [TestMethod]
    public void WithInit_AllPropertiesCanBeSet()
    {
        var duration = TimeSpan.FromMinutes(3.5);
        var result = new AudioCaptureResult
        {
            Success = true,
            FilePath = "/recordings/test.m4a",
            Duration = duration,
            ErrorMessage = null
        };

        Assert.IsTrue(result.Success);
        Assert.AreEqual("/recordings/test.m4a", result.FilePath);
        Assert.AreEqual(duration, result.Duration);
        Assert.IsNull(result.ErrorMessage);
    }

    [TestMethod]
    public void FailureResult_HasErrorMessage()
    {
        var result = new AudioCaptureResult
        {
            Success = false,
            ErrorMessage = "Recording failed."
        };

        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.ErrorMessage);
        Assert.AreEqual("Recording failed.", result.ErrorMessage);
    }
}
