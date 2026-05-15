using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Rizedown.Collections;

/// <summary>
/// ObservableCollection that can replace all items in one shot, firing a single Reset
/// CollectionChanged notification instead of N individual Adds. Use ReplaceAll when
/// bulk-refreshing lists bound to UI — avoids layout thrashing on large item counts.
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IReadOnlyList<T> items)
    {
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
