using Rizedown.Services;

namespace Rizedown.Services;

public sealed class SystemPlatformInfo : IPlatformInfo
{
    public bool IsMacCatalyst => OperatingSystem.IsMacCatalyst();
    public bool IsWindows => OperatingSystem.IsWindows();

    public string GetDefaultRecordingFolder()
    {
#if MACCATALYST
        // NSFileManager resolves to ~/Documents when unsandboxed, and to the app
        // container's Documents folder when sandboxed — both are the correct writable
        // location for that context.
        var urls = Foundation.NSFileManager.DefaultManager.GetUrls(
            Foundation.NSSearchPathDirectory.DocumentDirectory,
            Foundation.NSSearchPathDomain.User);
        var path = urls?.FirstOrDefault()?.Path;
        if (!string.IsNullOrEmpty(path))
            return System.IO.Path.Combine(path, "Rizedown");
#endif
        return System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Rizedown");
    }
}
