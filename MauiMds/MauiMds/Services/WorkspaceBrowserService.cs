using MauiMds.AudioCapture;
using MauiMds.Models;
using Microsoft.Extensions.Logging;

namespace MauiMds.Services;

public sealed class WorkspaceBrowserService : IWorkspaceBrowserService
{
    private static readonly string[] AllowedExtensions = [".md", ".mds", ".m4a", ".mp3", ".flac", ".wav"];
    private readonly IFolderPickerPlatformService _folderPicker;
    private readonly ILogger<WorkspaceBrowserService> _logger;

    public WorkspaceBrowserService(IFolderPickerPlatformService folderPicker, ILogger<WorkspaceBrowserService> logger)
    {
        _folderPicker = folderPicker;
        _logger = logger;
    }

    public Task<string?> PickFolderAsync() => _folderPicker.PickFolderAsync();

    public string? TryCreatePersistentAccessBookmark(string folderPath)
        => _folderPicker.TryCreatePersistentAccessBookmark(folderPath);

    public bool TryRestorePersistentAccessFromBookmark(string bookmark, out string? restoredPath, out bool isStale)
        => _folderPicker.TryRestorePersistentAccessFromBookmark(bookmark, out restoredPath, out isStale);

    public Task<IReadOnlyList<WorkspaceNodeInfo>> LoadWorkspaceTreeAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<WorkspaceNodeInfo>>(() =>
        {
            var rootDirectory = new DirectoryInfo(rootPath);
            if (!rootDirectory.Exists)
            {
                return [];
            }

            var children = BuildChildren(rootDirectory, cancellationToken);
            return children;
        }, cancellationToken);
    }

    public async Task<MarkdownDocument> LoadDocumentAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var extension = Path.GetExtension(filePath);
        if (!AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Please choose a .mds or .md file.");
        }

        var fileInfo = new FileInfo(filePath);
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);

        return new MarkdownDocument
        {
            FilePath = filePath,
            FileName = fileInfo.Name,
            FileSizeBytes = fileInfo.Length,
            LastModified = fileInfo.LastWriteTimeUtc,
            Content = content
        };
    }

    public async Task<bool> FileContainsTextAsync(string filePath, string query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var extension = Path.GetExtension(filePath);
        if (!AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        return content.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> CreateMarkdownSharpFileAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(directoryPath);

        var index = 0;
        string fileName;
        string fullPath;

        do
        {
            fileName = index == 0 ? "New_Markdown_Sharp.mds" : $"New_Markdown_Sharp_{index}.mds";
            fullPath = Path.Combine(directoryPath, fileName);
            index++;
        }
        while (File.Exists(fullPath));

        await File.WriteAllTextAsync(fullPath, string.Empty, cancellationToken);
        _logger.LogInformation("Created markdown file {FilePath}", fullPath);
        return fullPath;
    }

    public Task<string> RenameMarkdownFileAsync(string filePath, string newFileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sanitizedFileName = newFileName.Trim();
        if (string.IsNullOrWhiteSpace(sanitizedFileName))
        {
            throw new InvalidOperationException("File name cannot be empty.");
        }

        var extension = Path.GetExtension(sanitizedFileName);
        if (!AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Markdown files must keep a .md or .mds extension.");
        }

        var directoryPath = Path.GetDirectoryName(filePath) ?? throw new InvalidOperationException("Unable to determine the file directory.");
        var targetPath = Path.Combine(directoryPath, sanitizedFileName);

        if (string.Equals(filePath, targetPath, StringComparison.Ordinal))
        {
            return Task.FromResult(filePath);
        }

        if (File.Exists(targetPath))
        {
            throw new InvalidOperationException("A file with that name already exists.");
        }

        File.Move(filePath, targetPath);
        _logger.LogInformation("Renamed markdown file from {OldPath} to {NewPath}", filePath, targetPath);
        return Task.FromResult(targetPath);
    }

    public Task RenameRecordingGroupAsync(RecordingGroup group, string newBaseName)
    {
        var newBase = newBaseName.Trim();
        if (string.IsNullOrWhiteSpace(newBase))
            throw new InvalidOperationException("Name cannot be empty.");
        if (newBase.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException("Name contains invalid characters.");
        if (string.Equals(newBase, group.BaseName, StringComparison.Ordinal))
            return Task.CompletedTask;

        // Check for conflicts before moving anything
        foreach (var file in Directory.GetFiles(group.DirectoryPath))
        {
            var fileName = Path.GetFileName(file);
            if (!fileName.StartsWith(group.BaseName, StringComparison.OrdinalIgnoreCase)) continue;
            var suffix = fileName[group.BaseName.Length..];
            var newFilePath = Path.Combine(group.DirectoryPath, newBase + suffix);
            if (File.Exists(newFilePath))
                throw new InvalidOperationException($"A file named '{newBase + suffix}' already exists.");
        }

        foreach (var file in Directory.GetFiles(group.DirectoryPath))
        {
            var fileName = Path.GetFileName(file);
            if (!fileName.StartsWith(group.BaseName, StringComparison.OrdinalIgnoreCase)) continue;
            var suffix = fileName[group.BaseName.Length..];
            File.Move(file, Path.Combine(group.DirectoryPath, newBase + suffix));
        }

        _logger.LogInformation("Renamed recording group from {OldBase} to {NewBase}", group.BaseName, newBase);
        return Task.CompletedTask;
    }

    private List<WorkspaceNodeInfo> BuildChildren(DirectoryInfo directory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var directories = EnumerateDirectoriesSafely(directory)
            .Where(child => !IsHidden(child))
            .OrderBy(child => child.Name, StringComparer.OrdinalIgnoreCase)
            .Select(child =>
            {
                var children = BuildChildren(child, cancellationToken);
                return new WorkspaceNodeInfo
                {
                    FullPath = child.FullName,
                    IsDirectory = true,
                    Children = children
                };
            });

        var allFiles = EnumerateFilesSafely(directory)
            .Where(file => !IsHidden(file))
            .Where(file => AllowedExtensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase))
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var fileNodes = BuildFileNodes(directory.FullName, allFiles);

        return [.. directories, .. fileNodes];
    }

    private static List<WorkspaceNodeInfo> BuildFileNodes(string directoryPath, List<FileInfo> files)
    {
        var groups = new Dictionary<string, (string? Mic, string? Sys, string? Transcript)>(
            StringComparer.OrdinalIgnoreCase);
        var standaloneFiles = new List<FileInfo>();

        foreach (var file in files)
        {
            if (RecordingPathBuilder.TryParseGroupFile(file.Name, out var baseName, out var role))
            {
                if (!groups.TryGetValue(baseName, out var entry))
                    entry = (null, null, null);

                entry = role switch
                {
                    "mic"        => entry with { Mic = file.FullName },
                    "sys"        => entry with { Sys = file.FullName },
                    "transcript" => entry with { Transcript = file.FullName },
                    _            => entry
                };
                groups[baseName] = entry;
            }
            else
            {
                standaloneFiles.Add(file);
            }
        }

        var result = new List<WorkspaceNodeInfo>();

        foreach (var (baseName, (mic, sys, transcript)) in groups.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var group = new RecordingGroup
            {
                BaseName = baseName,
                DirectoryPath = directoryPath,
                MicFilePath = mic,
                SysFilePath = sys,
                TranscriptPath = transcript
            };

            var canonicalPath = mic ?? sys ?? transcript ?? Path.Combine(directoryPath, baseName);

            result.Add(new WorkspaceNodeInfo
            {
                FullPath = canonicalPath,
                IsDirectory = false,
                Children = [],
                RecordingGroup = group
            });
        }

        foreach (var file in standaloneFiles)
        {
            result.Add(new WorkspaceNodeInfo
            {
                FullPath = file.FullName,
                IsDirectory = false,
                Children = []
            });
        }

        result.Sort((a, b) =>
        {
            var nameA = a.RecordingGroup?.BaseName ?? Path.GetFileName(a.FullPath);
            var nameB = b.RecordingGroup?.BaseName ?? Path.GetFileName(b.FullPath);
            return StringComparer.OrdinalIgnoreCase.Compare(nameA, nameB);
        });

        return result;
    }

    private IEnumerable<DirectoryInfo> EnumerateDirectoriesSafely(DirectoryInfo directory)
    {
        try
        {
            return directory.EnumerateDirectories();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Skipping unreadable workspace directory {DirectoryPath}", directory.FullName);
            return [];
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Skipping workspace directory {DirectoryPath} because it could not be enumerated.", directory.FullName);
            return [];
        }
    }

    private IEnumerable<FileInfo> EnumerateFilesSafely(DirectoryInfo directory)
    {
        try
        {
            return directory.EnumerateFiles();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Skipping files in unreadable workspace directory {DirectoryPath}", directory.FullName);
            return [];
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Skipping files in workspace directory {DirectoryPath} because they could not be enumerated.", directory.FullName);
            return [];
        }
    }

    private static bool IsHidden(FileSystemInfo fileSystemInfo)
    {
        if (fileSystemInfo.Name.StartsWith(".", StringComparison.Ordinal))
        {
            return true;
        }

        var attributes = fileSystemInfo.Attributes;
        return attributes.HasFlag(FileAttributes.Hidden) || attributes.HasFlag(FileAttributes.System);
    }
}
