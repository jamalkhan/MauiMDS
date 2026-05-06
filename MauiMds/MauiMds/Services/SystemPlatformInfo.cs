using MauiMds.Services;

namespace MauiMds.Services;

public sealed class SystemPlatformInfo : IPlatformInfo
{
    public bool IsMacCatalyst => OperatingSystem.IsMacCatalyst();
    public bool IsWindows => OperatingSystem.IsWindows();
}
