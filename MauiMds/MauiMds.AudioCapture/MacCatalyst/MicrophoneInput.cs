using AVFoundation;
using CoreMedia;
using Foundation;
using Microsoft.Extensions.Logging;

namespace MauiMds.AudioCapture.MacCatalyst;

/// <summary>
/// Captures the local microphone via AVCaptureSession.
/// </summary>
internal sealed class MicrophoneInput : IDisposable
{
    private readonly ILogger _logger;
    private AVCaptureSession? _session;
    private MicSampleDelegate? _delegate;
    private bool _disposed;

    public event Action<CMSampleBuffer>? SampleBufferReceived;

    public MicrophoneInput(ILogger logger)
    {
        _logger = logger;
    }

    public void Start()
    {
        var micDevice = AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Audio)
            ?? throw new InvalidOperationException(
                "No microphone device found. Ensure a microphone is connected and permission is granted.");

        var micInput = AVCaptureDeviceInput.FromDevice(micDevice, out var inputError);
        if (inputError is not null)
        {
            throw new InvalidOperationException(
                $"Could not create microphone input: {inputError.LocalizedDescription}");
        }

        _delegate = new MicSampleDelegate();
        _delegate.SampleBufferReceived += buf => SampleBufferReceived?.Invoke(buf);

        var audioOutput = new AVCaptureAudioDataOutput();
        var captureQueue = new CoreFoundation.DispatchQueue("com.mauimds.audiocapture.mic", false);
        audioOutput.SetSampleBufferDelegate(_delegate, captureQueue);

        _session = new AVCaptureSession();
        _session.BeginConfiguration();
        _session.AddInput(micInput!);
        _session.AddOutput(audioOutput);
        _session.CommitConfiguration();
        _session.StartRunning();

        _logger.LogInformation("MicrophoneInput: capture started.");
    }

    public void Stop()
    {
        if (_session is null)
        {
            return;
        }

        _session.StopRunning();
        _session.Dispose();
        _session = null;
        _logger.LogInformation("MicrophoneInput: capture stopped.");
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Stop();
            _delegate?.Dispose();
            _disposed = true;
        }
    }

    // ---- inner delegate ----

    private sealed class MicSampleDelegate : AVCaptureAudioDataOutputSampleBufferDelegate
    {
        public event Action<CMSampleBuffer>? SampleBufferReceived;

        public override void DidOutputSampleBuffer(
            AVCaptureOutput captureOutput,
            CMSampleBuffer sampleBuffer,
            AVCaptureConnection connection)
        {
            SampleBufferReceived?.Invoke(sampleBuffer);
        }
    }
}
