using Microsoft.Extensions.Logging;

namespace MauiMds.Logging;

public sealed class FileLogLevelSwitch
{
    private int _minimumLevel;

    public FileLogLevelSwitch(LogLevel minimumLevel)
    {
        MinimumLevel = minimumLevel;
    }

    public LogLevel MinimumLevel
    {
        get => (LogLevel)Volatile.Read(ref _minimumLevel);
        set => Volatile.Write(ref _minimumLevel, (int)value);
    }
}
