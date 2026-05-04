#pragma warning disable CA1416  // WasapiLoopbackCapture requires Windows 10+; this file is Windows-only.
using NAudio.Wave;

namespace MauiMds.AudioCapture.Windows;

/// <summary>
/// Captures system audio (all playing audio from the default output device) via WASAPI loopback.
/// No special permissions are required on Windows for loopback capture.
/// </summary>
internal sealed class SystemAudioLoopback : IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private WaveFileWriter? _writer;

    public void Start(string tempWavPath)
    {
        _capture = new WasapiLoopbackCapture();
        _writer = new WaveFileWriter(tempWavPath, _capture.WaveFormat);
        _capture.DataAvailable += OnDataAvailable;
        _capture.StartRecording();
    }

    public void Stop()
    {
        if (_capture is null) return;
        _capture.StopRecording(); // blocks until capture thread exits
        _capture.DataAvailable -= OnDataAvailable;
        _writer?.Flush();
        _writer?.Dispose();
        _writer = null;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
        => _writer?.Write(e.Buffer, 0, e.BytesRecorded);

    public void Dispose()
    {
        _writer?.Dispose();
        _capture?.Dispose();
    }
}
