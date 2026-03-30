namespace MauiMds.Services;

public sealed class DocumentWatchService : IDocumentWatchService
{
    private FileSystemWatcher? _watcher;
    private string? _watchedFilePath;

    public event EventHandler<string>? DocumentChanged;

    public void Watch(string? filePath)
    {
        Stop();

        if (string.IsNullOrWhiteSpace(filePath) || !Path.IsPathRooted(filePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(filePath);
        var fileName = Path.GetFileName(filePath);

        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName) || !Directory.Exists(directory))
        {
            return;
        }

        _watchedFilePath = filePath;
        _watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _watcher.Changed += OnWatcherChanged;
        _watcher.Created += OnWatcherChanged;
        _watcher.Renamed += OnWatcherRenamed;
        _watcher.Deleted += OnWatcherDeleted;
    }

    public void Stop()
    {
        if (_watcher is null)
        {
            return;
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnWatcherChanged;
        _watcher.Created -= OnWatcherChanged;
        _watcher.Renamed -= OnWatcherRenamed;
        _watcher.Deleted -= OnWatcherDeleted;
        _watcher.Dispose();
        _watcher = null;
        _watchedFilePath = null;
    }

    public void Dispose()
    {
        Stop();
    }

    private void OnWatcherChanged(object sender, FileSystemEventArgs e)
    {
        if (_watchedFilePath is null)
        {
            return;
        }

        DocumentChanged?.Invoke(this, _watchedFilePath);
    }

    private void OnWatcherRenamed(object sender, RenamedEventArgs e)
    {
        _watchedFilePath = e.FullPath;
        DocumentChanged?.Invoke(this, e.FullPath);
    }

    private void OnWatcherDeleted(object sender, FileSystemEventArgs e)
    {
        if (_watchedFilePath is null)
        {
            return;
        }

        DocumentChanged?.Invoke(this, _watchedFilePath);
    }
}
