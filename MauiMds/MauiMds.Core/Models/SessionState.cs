namespace MauiMds.Models;

public sealed class SessionState
{
    public string? WorkspaceRootPath { get; set; }
    public string? WorkspaceRootBookmark { get; set; }
    public string? DocumentFilePath { get; set; }
    public string? DocumentFileBookmark { get; set; }
    public string? CurrentFolderPath { get; set; }
    public EditorViewMode LastViewMode { get; set; } = EditorViewMode.Viewer;
    public bool IsWorkspacePanelVisible { get; set; }
    public double WorkspacePanelWidth { get; set; } = 260; // matches DefaultWorkspacePanelWidth in MainViewModel
}
