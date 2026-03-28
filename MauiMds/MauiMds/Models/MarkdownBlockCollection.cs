using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace MauiMds.Models;

public sealed class MarkdownBlockCollection : ObservableCollection<MarkdownBlock>
{
    public void ReplaceAll(IEnumerable<MarkdownBlock> blocks)
    {
        Items.Clear();

        foreach (var block in blocks)
        {
            Items.Add(block);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
