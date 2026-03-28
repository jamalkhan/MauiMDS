using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MauiMds.Models;

public sealed class WorkspaceTreeItem : INotifyPropertyChanged
{
    private string _fullPath;
    private bool _isExpanded = true;
    private bool _isSelected;
    private bool _isRenaming;
    private string _renameText;

    public WorkspaceTreeItem(string fullPath, bool isDirectory, int depth, WorkspaceTreeItem? parent = null)
    {
        _fullPath = fullPath;
        IsDirectory = isDirectory;
        Depth = depth;
        Parent = parent;
        _renameText = Name;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public WorkspaceTreeItem? Parent { get; }
    public List<WorkspaceTreeItem> Children { get; } = [];
    public bool IsDirectory { get; }
    public int Depth { get; }
    public double IndentWidth => Depth * 14;
    public bool HasChildren => Children.Count > 0;
    public bool CanRename => !IsDirectory;
    public string ExpandGlyph => IsExpanded ? "▾" : "▸";
    public string ItemGlyph => IsDirectory ? "DIR" : "MD";

    public string FullPath
    {
        get => _fullPath;
        private set
        {
            if (_fullPath == value)
            {
                return;
            }

            _fullPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Name));
        }
    }

    public string Name => Path.GetFileName(FullPath);

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

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
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public bool IsRenaming
    {
        get => _isRenaming;
        set
        {
            if (_isRenaming == value)
            {
                return;
            }

            _isRenaming = value;
            OnPropertyChanged();
        }
    }

    public string RenameText
    {
        get => _renameText;
        set
        {
            if (_renameText == value)
            {
                return;
            }

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
