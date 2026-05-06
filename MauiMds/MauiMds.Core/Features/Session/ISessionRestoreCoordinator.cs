using MauiMds.Models;

namespace MauiMds.Features.Session;

public interface ISessionRestoreCoordinator
{
    SessionState Load();
    void Save(SessionPersistenceRequest request);
    string? ResolveWorkspaceRestorePath(SessionState sessionState, out string? repickMessage);
    string? ResolveDocumentRestorePath(SessionState sessionState, bool hasWorkspaceAccess, out bool needsRepick);
}
