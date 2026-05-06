namespace MauiMds.AudioCapture;

public interface IAudioPlayerService
{
    string? CurrentlyPlayingPath { get; }
    bool IsPlaying { get; }
    TimeSpan Position { get; }
    TimeSpan Duration { get; }
    event EventHandler? PlaybackStateChanged;
    event EventHandler? PlaybackPositionChanged;
    Task PlayAsync(string filePath);
    void Pause();
    void Stop();
    void Seek(TimeSpan position);
}
