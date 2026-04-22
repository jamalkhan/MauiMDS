using CoreMedia;
using Foundation;
using ScreenCaptureKit;
using Microsoft.Extensions.Logging;

namespace MauiMds.AudioCapture.MacCatalyst;

/// <summary>
/// Captures system-wide audio (all apps, e.g. Teams/Zoom output) via ScreenCaptureKit.
/// Requires the user to have granted Screen Recording permission in System Settings.
/// </summary>
internal sealed class SystemAudioOutput : NSObject, ISCStreamOutput
{
    private readonly ILogger _logger;
    private SCStream? _stream;
    private bool _disposed;

    public event Action<CMSampleBuffer>? SampleBufferReceived;

    public SystemAudioOutput(ILogger logger)
    {
        _logger = logger;
    }

    public async Task StartAsync(int sampleRate, int channelCount)
    {
        _logger.LogInformation("SystemAudioOutput: requesting shareable content.");

        SCShareableContent content;
        try
        {
            content = await SCShareableContent.GetShareableContentAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "ScreenCaptureKit could not enumerate content. " +
                "Grant Screen Recording permission in System Settings > Privacy & Security.", ex);
        }

        var display = content.Displays.FirstOrDefault()
            ?? throw new InvalidOperationException("No display found — cannot create an SCContentFilter.");

        // Include all windows currently on the display so audio from every running
        // app (Teams, Zoom, browser, etc.) is captured.  SCContentFilterOption 0
        // is the default — no additional restriction beyond the window list.
        var filter = new SCContentFilter(display, content.Windows, (SCContentFilterOption)0);

        var config = new SCStreamConfiguration
        {
            CapturesAudio = true,
            ExcludesCurrentProcessAudio = true,
            SampleRate = sampleRate,
            ChannelCount = channelCount,
        };

        _stream = new SCStream(filter, config, null);

        var captureQueue = new CoreFoundation.DispatchQueue("com.mauimds.audiocapture.system", false);
        if (!_stream.AddStreamOutput(this, SCStreamOutputType.Audio, captureQueue, out var addError))
        {
            throw new InvalidOperationException(
                $"Failed to add audio stream output: {addError?.LocalizedDescription}");
        }

        await StartCaptureAsync(_stream);
        _logger.LogInformation("SystemAudioOutput: capture started.");
    }

    public async Task StopAsync()
    {
        if (_stream is null)
        {
            return;
        }

        try
        {
            await StopCaptureAsync(_stream);
            _logger.LogInformation("SystemAudioOutput: capture stopped.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SystemAudioOutput: error stopping capture.");
        }
        finally
        {
            _stream.Dispose();
            _stream = null;
        }
    }

    // ISCStreamOutput
    [Export("stream:didOutputSampleBuffer:ofType:")]
    public void DidOutputSampleBuffer(SCStream stream, CMSampleBuffer sampleBuffer, SCStreamOutputType outputType)
    {
        if (outputType == SCStreamOutputType.Audio)
        {
            SampleBufferReceived?.Invoke(sampleBuffer);
        }
    }

    // Wrap callback-based SCStream methods as Tasks.
    private static Task StartCaptureAsync(SCStream stream)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        stream.StartCapture(error =>
        {
            if (error is not null)
                tcs.SetException(new NSErrorException(error));
            else
                tcs.SetResult(true);
        });
        return tcs.Task;
    }

    private static Task StopCaptureAsync(SCStream stream)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        stream.StopCapture(error =>
        {
            if (error is not null)
                tcs.SetException(new NSErrorException(error));
            else
                tcs.SetResult(true);
        });
        return tcs.Task;
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _stream?.Dispose();
            }
            _disposed = true;
        }
        base.Dispose(disposing);
    }
}
