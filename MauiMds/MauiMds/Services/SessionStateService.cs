using System.Text.Json;
using MauiMds.Logging;
using MauiMds.Models;

namespace MauiMds.Services;

public sealed class SessionStateService : ISessionStateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public SessionState Load()
    {
        try
        {
            if (!File.Exists(LogPaths.SessionStateFilePath))
            {
                return new SessionState();
            }

            var json = File.ReadAllText(LogPaths.SessionStateFilePath);
            return JsonSerializer.Deserialize<SessionState>(json, JsonOptions) ?? new SessionState();
        }
        catch
        {
            return new SessionState();
        }
    }

    public void Save(SessionState state)
    {
        var directory = Path.GetDirectoryName(LogPaths.SessionStateFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(LogPaths.SessionStateFilePath, json);
    }
}
