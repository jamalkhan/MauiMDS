namespace MauiMds.AudioCapture;

public static class RecordingPathBuilder
{
    public const string RecordingsFolderName = "Recordings";

    public static string Build(string baseFolder, DateTimeOffset timestamp)
    {
        var fileName = $"audio_capture_{timestamp:yyyy-MM-dd HHmmss}.m4a";
        return Path.Combine(baseFolder, RecordingsFolderName, fileName);
    }
}
