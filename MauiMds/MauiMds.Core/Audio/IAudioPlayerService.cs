namespace MauiMds.AudioCapture;

public interface IAudioPlayerService
{
    string? CurrentlyPlayingPath { get; }
    bool IsPlaying { get; }
    event EventHandler? PlaybackStateChanged;
    Task PlayAsync(string filePath);
    void Pause();
    void Stop();
}
