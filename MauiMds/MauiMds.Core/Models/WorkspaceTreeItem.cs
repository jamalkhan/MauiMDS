using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MauiMds.Models;

public sealed class WorkspaceTreeItem : INotifyPropertyChanged
{
    private string _fullPath;
    private bool _isExpanded = true;
    private bool _isSelected;
    private bool _isRenaming;
    private bool _isPendingDelete;
    private bool _isActivelyRecording;
    private bool _isInTranscriptionQueue;
    private bool _isActivelyTranscribing;
    private bool _isActivelyDiarizing;
    private string _renameText;

    public WorkspaceTreeItem(string fullPath, bool isDirectory, int depth, WorkspaceTreeItem? parent = null,
        RecordingGroup? recordingGroup = null)
    {
        _fullPath = fullPath;
        IsDirectory = isDirectory;
        Depth = depth;
        Parent = parent;
        RecordingGroup = recordingGroup;
        _renameText = Name;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public WorkspaceTreeItem? Parent { get; }
    public List<WorkspaceTreeItem> Children { get; } = [];
    public bool IsDirectory { get; }
    public int Depth { get; }
    public double IndentWidth => Depth * 14;
    public bool HasChildren => Children.Count > 0;
    public bool CanDelete => !IsDirectory;
    public bool CanRename => !IsDirectory && !IsPendingDelete;
    public string ExpandGlyph => IsExpanded ? "▾" : "▸";
    public string SecondaryText => IsDirectory ? "Folder" : Path.GetDirectoryName(FullPath) ?? string.Empty;

    /// <summary>Non-null when this item represents a recording group in the workspace tree.</summary>
    public RecordingGroup? RecordingGroup { get; }

    public bool IsRecordingGroup => RecordingGroup is not null;

    public WorkspaceItemIconKind ItemIconKind
    {
        get
        {
            if (IsRecordingGroup)
            {
                if (_isInTranscriptionQueue) return WorkspaceItemIconKind.AudioQueued;
                return RecordingGroup!.HasTranscript
                    ? WorkspaceItemIconKind.AudioTranscribed
                    : WorkspaceItemIconKind.Audio;
            }

            if (IsDirectory)
            {
                return string.Equals(Name, "Recordings", StringComparison.OrdinalIgnoreCase)
                    ? WorkspaceItemIconKind.RecordingsFolder
                    : WorkspaceItemIconKind.Folder;
            }

            var ext = Path.GetExtension(FullPath);
            if (IsAudioExtension(ext))
                return WorkspaceItemIconKind.Audio;
            if (string.Equals(ext, ".mds", StringComparison.OrdinalIgnoreCase))
                return WorkspaceItemIconKind.MarkdownSharp;
            return WorkspaceItemIconKind.Markdown;
        }
    }

    public bool IsAudioFile => !IsDirectory && !IsRecordingGroup && IsAudioExtension(Path.GetExtension(FullPath));

    private static bool IsAudioExtension(string ext) =>
        string.Equals(ext, ".m4a", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ext, ".mp3", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ext, ".flac", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ext, ".wav", StringComparison.OrdinalIgnoreCase);

    public string FullPath
    {
        get => _fullPath;
        private set
        {
            if (_fullPath == value) return;
            _fullPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(SecondaryText));
        }
    }

    public string Name => IsRecordingGroup
        ? RecordingGroup!.DisplayName
        : Path.GetFileName(FullPath);

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExpandGlyph));
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public bool IsRenaming
    {
        get => _isRenaming;
        set
        {
            if (_isRenaming == value) return;
            _isRenaming = value;
            OnPropertyChanged();
        }
    }

    public bool IsPendingDelete
    {
        get => _isPendingDelete;
        set
        {
            if (_isPendingDelete == value) return;
            _isPendingDelete = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRename));
        }
    }

    public bool IsActivelyRecording
    {
        get => _isActivelyRecording;
        set
        {
            if (_isActivelyRecording == value) return;
            _isActivelyRecording = value;
            OnPropertyChanged();
        }
    }

    public bool IsInTranscriptionQueue
    {
        get => _isInTranscriptionQueue;
        set
        {
            if (_isInTranscriptionQueue == value) return;
            _isInTranscriptionQueue = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ItemIconKind));
        }
    }

    public bool IsActivelyTranscribing
    {
        get => _isActivelyTranscribing;
        set
        {
            if (_isActivelyTranscribing == value) return;
            _isActivelyTranscribing = value;
            OnPropertyChanged();
        }
    }

    public bool IsActivelyDiarizing
    {
        get => _isActivelyDiarizing;
        set
        {
            if (_isActivelyDiarizing == value) return;
            _isActivelyDiarizing = value;
            OnPropertyChanged();
        }
    }

    public string RenameText
    {
        get => _renameText;
        set
        {
            if (_renameText == value) return;
            _renameText = value;
            OnPropertyChanged();
        }
    }

    public void UpdateFullPath(string newFullPath)
    {
        FullPath = newFullPath;
        RenameText = Name;
    }

    public void ResetRenameText()
    {
        RenameText = Name;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
