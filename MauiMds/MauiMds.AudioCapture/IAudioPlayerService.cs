namespace MauiMds.AudioCapture;

public interface IAudioPlayerService
{
    /// <summary>Full path of the file currently playing, or null when idle.</summary>
    string? CurrentlyPlayingPath { get; }

    bool IsPlaying { get; }

    /// <summary>Fired on the main thread whenever <see cref="IsPlaying"/> or <see cref="CurrentlyPlayingPath"/> changes.</summary>
    event EventHandler? PlaybackStateChanged;

    /// <summary>
    /// Starts playback of <paramref name="filePath"/>.
    /// If a different file is already playing it is stopped first.
    /// If the same file is already playing, this is a no-op.
    /// </summary>
    Task PlayAsync(string filePath);

    /// <summary>Pauses or stops the current playback. No-op when already idle.</summary>
    void Pause();

    void Stop();
}
