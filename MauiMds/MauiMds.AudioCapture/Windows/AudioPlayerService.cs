using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace MauiMds.AudioCapture.Windows;

public sealed class AudioPlayerService : IAudioPlayerService, IDisposable
{
    private readonly ILogger<AudioPlayerService> _logger;
    private readonly SynchronizationContext? _syncContext;
    private WaveOutEvent? _output;
    private AudioFileReader? _reader;
    private string? _currentPath;
    private bool _disposed;

    public string? CurrentlyPlayingPath => _currentPath;
    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;

    public event EventHandler? PlaybackStateChanged;

    public AudioPlayerService(ILogger<AudioPlayerService> logger)
    {
        _logger = logger;
        _syncContext = SynchronizationContext.Current;
    }

    public Task PlayAsync(string filePath)
    {
        if (string.Equals(_currentPath, filePath, StringComparison.Ordinal) && IsPlaying)
            return Task.CompletedTask;

        Stop();

        try
        {
            _reader = new AudioFileReader(filePath);
            _output = new WaveOutEvent();
            _output.Init(_reader);
            _output.PlaybackStopped += OnPlaybackStopped;
            _output.Play();
            _currentPath = filePath;
            _logger.LogInformation("AudioPlayerService: playing {Path}", filePath);
            RaiseStateChanged();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AudioPlayerService: exception starting playback of {Path}", filePath);
            Stop();
        }

        return Task.CompletedTask;
    }

    public void Pause()
    {
        if (_output?.PlaybackState != PlaybackState.Playing) return;
        _output.Pause();
        _logger.LogInformation("AudioPlayerService: paused.");
        RaiseStateChanged();
    }

    public void Stop()
    {
        if (_output is null) return;
        _output.PlaybackStopped -= OnPlaybackStopped;
        _output.Stop();
        _output.Dispose();
        _output = null;
        _reader?.Dispose();
        _reader = null;
        _currentPath = null;
        _logger.LogInformation("AudioPlayerService: stopped.");
        RaiseStateChanged();
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        _output?.Dispose();
        _output = null;
        _reader?.Dispose();
        _reader = null;
        _currentPath = null;
        _logger.LogInformation("AudioPlayerService: playback finished.");
        RaiseStateChanged();
    }

    private void RaiseStateChanged()
    {
        if (_syncContext is not null)
            _syncContext.Post(_ => PlaybackStateChanged?.Invoke(this, EventArgs.Empty), null);
        else
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }
}
