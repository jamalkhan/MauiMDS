using MauiMds.Models;

namespace MauiMds.Transcription;

public sealed class FileTranscriptStorage : ITranscriptStorage
{
    public string GetTranscriptPath(RecordingGroup group)
        => Path.Combine(group.DirectoryPath, group.BaseName + "_transcript.md");

    public string GetRotatedPath(string existingPath)
    {
        var dir  = Path.GetDirectoryName(existingPath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(existingPath);
        var ext  = Path.GetExtension(existingPath);

        var candidate = Path.Combine(dir, $"{stem}.old{ext}");
        if (!File.Exists(candidate)) return candidate;

        for (var i = 1; ; i++)
        {
            candidate = Path.Combine(dir, $"{stem}.old.{i}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    public Task WriteAsync(string path, string content)
        => File.WriteAllTextAsync(path, content);

    public bool Exists(string path) => File.Exists(path);

    public void Move(string sourcePath, string destPath)
        => File.Move(sourcePath, destPath);
}
