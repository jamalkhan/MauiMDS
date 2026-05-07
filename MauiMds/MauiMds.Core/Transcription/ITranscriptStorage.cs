using MauiMds.Models;

namespace MauiMds.Transcription;

public interface ITranscriptStorage
{
    string GetTranscriptPath(RecordingGroup group);
    string GetRotatedPath(string existingPath);
    Task WriteAsync(string path, string content);
    bool Exists(string path);
    void Move(string sourcePath, string destPath);
}
