using MauiMds.Services;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MauiMds.Services;

internal sealed class FolderPickerPlatformService : IFolderPickerPlatformService
{
    public async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        var platformView = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView;
        if (platformView is not null)
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(platformView));
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    public string? TryCreatePersistentAccessBookmark(string folderPath) => null;

    public bool TryRestorePersistentAccessFromBookmark(string bookmark, out string? restoredPath, out bool isStale)
    {
        restoredPath = null;
        isStale = false;
        return false;
    }
}
