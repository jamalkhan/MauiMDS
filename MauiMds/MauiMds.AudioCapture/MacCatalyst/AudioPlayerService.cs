using AVFoundation;
using CoreFoundation;
using Foundation;
using Microsoft.Extensions.Logging;

namespace MauiMds.AudioCapture.MacCatalyst;

public sealed class AudioPlayerService : IAudioPlayerService, IDisposable
{
    private readonly ILogger<AudioPlayerService> _logger;
    private AVAudioPlayer? _player;
    private string? _currentPath;
    private bool _disposed;

    public string? CurrentlyPlayingPath => _currentPath;
    public bool IsPlaying => _player?.Playing ?? false;

    public event EventHandler? PlaybackStateChanged;

    public AudioPlayerService(ILogger<AudioPlayerService> logger)
    {
        _logger = logger;
    }

    public Task PlayAsync(string filePath)
    {
        if (string.Equals(_currentPath, filePath, StringComparison.Ordinal) && IsPlaying)
            return Task.CompletedTask;

        Stop();

        try
        {
            var url = NSUrl.FromFilename(filePath);
            _player = AVAudioPlayer.FromUrl(url);
            if (_player is null)
            {
                _logger.LogError("AudioPlayerService: could not create player for {Path}", filePath);
                return Task.CompletedTask;
            }

            _player.FinishedPlaying += OnFinishedPlaying;
            _player.PrepareToPlay();
            _player.Play();
            _currentPath = filePath;

            _logger.LogInformation("AudioPlayerService: playing {Path}", filePath);
            RaiseStateChanged();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AudioPlayerService: exception starting playback of {Path}", filePath);
        }

        return Task.CompletedTask;
    }

    public void Pause()
    {
        if (_player is null || !_player.Playing) return;
        _player.Pause();
        _logger.LogInformation("AudioPlayerService: paused.");
        RaiseStateChanged();
    }

    public void Stop()
    {
        if (_player is null) return;
        _player.FinishedPlaying -= OnFinishedPlaying;
        _player.Stop();
        _player.Dispose();
        _player = null;
        _currentPath = null;
        _logger.LogInformation("AudioPlayerService: stopped.");
        RaiseStateChanged();
    }

    private void OnFinishedPlaying(object? sender, AVStatusEventArgs e)
    {
        DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            _player?.Dispose();
            _player = null;
            _currentPath = null;
            _logger.LogInformation("AudioPlayerService: playback finished.");
            RaiseStateChanged();
        });
    }

    private void RaiseStateChanged() =>
        DispatchQueue.MainQueue.DispatchAsync(() => PlaybackStateChanged?.Invoke(this, EventArgs.Empty));

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }
}
