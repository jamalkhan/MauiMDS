using System.Text.RegularExpressions;

namespace MauiMds.AudioCapture;

public static class RecordingPathBuilder
{
    public const string RecordingsFolderName = "Recordings";

    public static string Build(string baseFolder, DateTimeOffset timestamp)
    {
        var fileName = $"audio_capture_{timestamp:yyyy_MM_dd_HHmmss}.m4a";
        return Path.Combine(baseFolder, RecordingsFolderName, fileName);
    }

    public static string BuildMic(string baseFolder, DateTimeOffset timestamp, string extension = ".m4a")
    {
        var fileName = $"audio_capture_{timestamp:yyyy_MM_dd_HHmmss}_mic{extension}";
        return Path.Combine(baseFolder, RecordingsFolderName, fileName);
    }

    public static string BuildSys(string baseFolder, DateTimeOffset timestamp, string extension = ".m4a")
    {
        var fileName = $"audio_capture_{timestamp:yyyy_MM_dd_HHmmss}_sys{extension}";
        return Path.Combine(baseFolder, RecordingsFolderName, fileName);
    }

    public static string BuildTranscript(string baseFolder, DateTimeOffset timestamp)
    {
        var fileName = $"audio_capture_{timestamp:yyyy_MM_dd_HHmmss}_transcript.md";
        return Path.Combine(baseFolder, RecordingsFolderName, fileName);
    }

    private static readonly Regex GroupFilePattern = new(
        @"^(.+)_(mic|sys|transcript)(\..+)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TimestampPattern = new(
        @"audio_capture_(\d{4})_(\d{2})_(\d{2})_(\d{6})$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool TryParseRecordingStart(string baseName, out DateTime startTime)
    {
        var m = TimestampPattern.Match(baseName);
        if (!m.Success) { startTime = default; return false; }

        var time = m.Groups[4].Value;
        try
        {
            startTime = new DateTime(
                int.Parse(m.Groups[1].Value),
                int.Parse(m.Groups[2].Value),
                int.Parse(m.Groups[3].Value),
                int.Parse(time[0..2]),
                int.Parse(time[2..4]),
                int.Parse(time[4..6]),
                DateTimeKind.Local);
            return true;
        }
        catch { startTime = default; return false; }
    }

    public static bool TryParseGroupFile(string fileName, out string baseName, out string role)
    {
        var m = GroupFilePattern.Match(fileName);
        if (m.Success)
        {
            baseName = m.Groups[1].Value;
            role = m.Groups[2].Value.ToLowerInvariant();
            return true;
        }
        baseName = string.Empty;
        role = string.Empty;
        return false;
    }
}
