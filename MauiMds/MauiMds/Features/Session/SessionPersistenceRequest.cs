using MauiMds.Models;

namespace MauiMds.Features.Session;

public sealed class SessionPersistenceRequest
{
    public string? WorkspaceRootPath { get; init; }
    public string? DocumentFilePath { get; init; }
    public string? CurrentFolderPath { get; init; }
    public EditorViewMode ViewMode { get; init; }
    public bool IsWorkspacePanelVisible { get; init; }
    public double WorkspacePanelWidth { get; init; }
}
