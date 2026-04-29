namespace MauiMds.Models;

public sealed class WorkspaceNodeInfo
{
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }
    public required List<WorkspaceNodeInfo> Children { get; init; }

    /// <summary>
    /// Set when this node represents a recording group (two audio files + optional transcript
    /// collapsed into one logical workspace item).
    /// </summary>
    public RecordingGroup? RecordingGroup { get; init; }
}
