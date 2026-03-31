namespace MauiMds.Models;

public sealed class WorkspaceNodeInfo
{
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }
    public required List<WorkspaceNodeInfo> Children { get; init; }
}
