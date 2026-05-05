namespace MauiMds.Services;

public interface IFolderPickerPlatformService
{
    Task<string?> PickFolderAsync();
    string? TryCreatePersistentAccessBookmark(string folderPath);
    bool TryRestorePersistentAccessFromBookmark(string bookmark, out string? restoredPath, out bool isStale);
}
