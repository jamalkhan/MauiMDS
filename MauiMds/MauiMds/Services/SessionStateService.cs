using System.Text.Json;
using MauiMds.Logging;
using MauiMds.Models;
using Microsoft.Extensions.Logging;

namespace MauiMds.Services;

public sealed class SessionStateService : ISessionStateService
{
    private readonly ILogger<SessionStateService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public SessionStateService(ILogger<SessionStateService> logger)
    {
        _logger = logger;
    }

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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SessionStateService: failed to load session state, starting fresh.");
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
