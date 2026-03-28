using MauiMds.Models;

namespace MauiMds.Services;

public interface IWorkspaceBrowserService
{
    Task<string?> PickFolderAsync();
    Task<IReadOnlyList<WorkspaceNodeInfo>> LoadWorkspaceTreeAsync(string rootPath, CancellationToken cancellationToken = default);
    Task<MarkdownDocument> LoadDocumentAsync(string filePath, CancellationToken cancellationToken = default);
    Task<bool> FileContainsTextAsync(string filePath, string query, CancellationToken cancellationToken = default);
    Task<string> CreateMarkdownSharpFileAsync(string directoryPath, CancellationToken cancellationToken = default);
    Task<string> RenameMarkdownFileAsync(string filePath, string newFileName, CancellationToken cancellationToken = default);
}
