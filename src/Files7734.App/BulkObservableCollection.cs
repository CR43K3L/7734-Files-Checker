using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Files7734.App;

/// <summary>Collection observable qui accepte l'ajout/suppression en bloc (une seule notification).</summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void InsertRange(int index, IList<T> items)
    {
        if (items.Count == 0) return;
        if (items.Count < 50)
        {
            for (int i = 0; i < items.Count; i++) Insert(index + i, items[i]);
            return;
        }
        var list = (List<T>)Items;
        list.InsertRange(index, items);
        RaiseReset();
    }

    public void RemoveRange(int index, int count)
    {
        if (count <= 0) return;
        if (count < 50)
        {
            for (int i = 0; i < count; i++) RemoveAt(index);
            return;
        }
        ((List<T>)Items).RemoveRange(index, count);
        RaiseReset();
    }

    public void ReplaceAll(IEnumerable<T> items)
    {
        var list = (List<T>)Items;
        list.Clear();
        list.AddRange(items);
        RaiseReset();
    }

    private void RaiseReset()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
