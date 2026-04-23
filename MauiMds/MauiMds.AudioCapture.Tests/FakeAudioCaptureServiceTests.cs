namespace MauiMds.AudioCapture.Tests;

[TestClass]
public sealed class FakeAudioCaptureServiceTests
{
    [TestMethod]
    public void InitialState_IsIdle()
    {
        var svc = new FakeAudioCaptureService();
        Assert.AreEqual(AudioCaptureState.Idle, svc.State);
    }

    [TestMethod]
    public async Task StartAsync_TransitionsToRecording()
    {
        var svc = new FakeAudioCaptureService();
        await svc.StartAsync(new AudioCaptureOptions { OutputPath = "/tmp/out.m4a" });
        Assert.AreEqual(AudioCaptureState.Recording, svc.State);
    }

    [TestMethod]
    public async Task StopAsync_AfterStart_TransitionsToIdle()
    {
        var svc = new FakeAudioCaptureService();
        await svc.StartAsync(new AudioCaptureOptions { OutputPath = "/tmp/out.m4a" });
        await svc.StopAsync();
        Assert.AreEqual(AudioCaptureState.Idle, svc.State);
    }

    [TestMethod]
    public async Task StopAsync_AfterStart_ReturnsSuccess()
    {
        var svc = new FakeAudioCaptureService();
        await svc.StartAsync(new AudioCaptureOptions { OutputPath = "/tmp/out.m4a" });
        var result = await svc.StopAsync();
        Assert.IsTrue(result.Success);
        Assert.IsNull(result.ErrorMessage);
    }

    [TestMethod]
    public async Task StopAsync_AfterStart_ReturnsOutputPath()
    {
        var svc = new FakeAudioCaptureService();
        const string path = "/tmp/test.m4a";
        await svc.StartAsync(new AudioCaptureOptions { OutputPath = path });
        var result = await svc.StopAsync();
        Assert.AreEqual(path, result.FilePath);
    }

    [TestMethod]
    public async Task StopAsync_WhenIdle_ReturnsFailure()
    {
        var svc = new FakeAudioCaptureService();
        var result = await svc.StopAsync();
        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.ErrorMessage);
    }

    [TestMethod]
    public async Task StartAsync_WhenAlreadyRecording_Throws()
    {
        var svc = new FakeAudioCaptureService();
        await svc.StartAsync(new AudioCaptureOptions { OutputPath = "/tmp/a.m4a" });
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => svc.StartAsync(new AudioCaptureOptions { OutputPath = "/tmp/b.m4a" }));
    }

    [TestMethod]
    public async Task StartAsync_WhenShouldThrow_Throws()
    {
        var svc = new FakeAudioCaptureService { ShouldThrowOnStart = true };
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => svc.StartAsync(new AudioCaptureOptions { OutputPath = "/tmp/a.m4a" }));
    }

    [TestMethod]
    public async Task StateChanged_FiresForEachTransition()
    {
        var svc = new FakeAudioCaptureService();
        var observed = new List<AudioCaptureState>();
        svc.StateChanged += (_, s) => observed.Add(s);

        await svc.StartAsync(new AudioCaptureOptions { OutputPath = "/tmp/out.m4a" });
        await svc.StopAsync();

        CollectionAssert.AreEqual(
            new[] { AudioCaptureState.Starting, AudioCaptureState.Recording, AudioCaptureState.Stopping, AudioCaptureState.Idle },
            observed);
    }

    [TestMethod]
    public async Task StartCallCount_IncreasesOnEachStart()
    {
        var svc = new FakeAudioCaptureService();
        await svc.StartAsync(new AudioCaptureOptions { OutputPath = "/tmp/a.m4a" });
        await svc.StopAsync();
        await svc.StartAsync(new AudioCaptureOptions { OutputPath = "/tmp/b.m4a" });
        Assert.AreEqual(2, svc.StartCallCount);
    }

    [TestMethod]
    public async Task StopCallCount_IncreasesOnEachStopAttempt()
    {
        var svc = new FakeAudioCaptureService();
        await svc.StopAsync(); // idle — fails but still counted
        await svc.StartAsync(new AudioCaptureOptions { OutputPath = "/tmp/a.m4a" });
        await svc.StopAsync();
        Assert.AreEqual(2, svc.StopCallCount);
    }

    [TestMethod]
    public async Task CheckMicrophonePermission_ReturnsConfiguredStatus()
    {
        var svc = new FakeAudioCaptureService { MicrophonePermission = AudioPermissionStatus.Denied };
        var status = await svc.CheckMicrophonePermissionAsync();
        Assert.AreEqual(AudioPermissionStatus.Denied, status);
    }

    [TestMethod]
    public async Task ShouldFailStop_ReturnsFailureResult()
    {
        var svc = new FakeAudioCaptureService { ShouldFailStop = true };
        await svc.StartAsync(new AudioCaptureOptions { OutputPath = "/tmp/out.m4a" });
        var result = await svc.StopAsync();
        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.ErrorMessage);
    }

    [TestMethod]
    public async Task StateHistory_TracksAllTransitions()
    {
        var svc = new FakeAudioCaptureService();
        await svc.StartAsync(new AudioCaptureOptions { OutputPath = "/tmp/out.m4a" });
        await svc.StopAsync();

        CollectionAssert.AreEqual(
            new[] { AudioCaptureState.Starting, AudioCaptureState.Recording, AudioCaptureState.Stopping, AudioCaptureState.Idle },
            svc.StateHistory);
    }
}
