using System.Text.RegularExpressions;

namespace MauiMds.AudioCapture;

public static class RecordingPathBuilder
{
    public const string RecordingsFolderName = "Recordings";

    // Legacy: single-file recording without source suffix (kept for old recordings on disk).
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
        var fileName = $"audio_capture_{timestamp:yyyy_MM_dd_HHmmss}_transcript.mds";
        return Path.Combine(baseFolder, RecordingsFolderName, fileName);
    }

    // Matches filenames like: audio_capture_2026_04_27_123456_mic.m4a
    private static readonly Regex GroupFilePattern = new(
        @"^(audio_capture_\d{4}_\d{2}_\d{2}_\d{6})_(mic|sys|transcript)(\..+)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Returns true and sets <paramref name="baseName"/> and <paramref name="role"/>
    /// ("mic", "sys", or "transcript") if <paramref name="fileName"/> belongs to a recording group.
    /// Pass the full filename including extension.
    /// </summary>
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
