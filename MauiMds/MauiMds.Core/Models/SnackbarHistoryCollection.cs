using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace MauiMds.Models;

public sealed class SnackbarHistoryCollection : ObservableCollection<SnackbarMessage>
{
    public void ReplaceAll(IEnumerable<SnackbarMessage> messages)
    {
        Items.Clear();

        foreach (var message in messages)
        {
            Items.Add(message);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
