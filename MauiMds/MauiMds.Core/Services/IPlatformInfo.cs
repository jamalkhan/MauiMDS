namespace MauiMds.Services;

public interface IPlatformInfo
{
    bool IsMacCatalyst { get; }
    bool IsWindows { get; }

    /// <summary>
    /// Returns the platform-appropriate default folder for recordings when no workspace root is set.
    /// On Mac Catalyst this uses NSFileManager to resolve the correct Documents directory; in a
    /// sandboxed build that is the app container's Documents folder (the only writable location
    /// outside a security-scoped bookmark). On Windows this is the user's My Documents folder.
    /// </summary>
    string GetDefaultRecordingFolder();
}
