namespace MauiMds.AudioCapture.Tests;

[TestClass]
public sealed class AudioCaptureEnumTests
{
    [TestMethod]
    public void AudioCaptureState_HasIdle()
        => Assert.IsTrue(Enum.IsDefined(typeof(AudioCaptureState), AudioCaptureState.Idle));

    [TestMethod]
    public void AudioCaptureState_HasStarting()
        => Assert.IsTrue(Enum.IsDefined(typeof(AudioCaptureState), AudioCaptureState.Starting));

    [TestMethod]
    public void AudioCaptureState_HasRecording()
        => Assert.IsTrue(Enum.IsDefined(typeof(AudioCaptureState), AudioCaptureState.Recording));

    [TestMethod]
    public void AudioCaptureState_HasStopping()
        => Assert.IsTrue(Enum.IsDefined(typeof(AudioCaptureState), AudioCaptureState.Stopping));

    [TestMethod]
    public void AudioCaptureState_HasExactlyFourValues()
        => Assert.AreEqual(4, Enum.GetValues<AudioCaptureState>().Length);

    [TestMethod]
    public void AudioPermissionStatus_HasNotDetermined()
        => Assert.IsTrue(Enum.IsDefined(typeof(AudioPermissionStatus), AudioPermissionStatus.NotDetermined));

    [TestMethod]
    public void AudioPermissionStatus_HasGranted()
        => Assert.IsTrue(Enum.IsDefined(typeof(AudioPermissionStatus), AudioPermissionStatus.Granted));

    [TestMethod]
    public void AudioPermissionStatus_HasDenied()
        => Assert.IsTrue(Enum.IsDefined(typeof(AudioPermissionStatus), AudioPermissionStatus.Denied));

    [TestMethod]
    public void AudioPermissionStatus_HasExactlyThreeValues()
        => Assert.AreEqual(3, Enum.GetValues<AudioPermissionStatus>().Length);
}
