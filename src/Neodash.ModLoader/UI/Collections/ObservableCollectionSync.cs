using System.Collections.ObjectModel;

namespace Neodash_Mod_Loader.Ui;

/// <summary>Updates list membership and order without resetting unchanged item containers.</summary>
internal static class ObservableCollectionSync
{
    internal static void Update<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        List<T> desired = [.. items];
        var retained = new HashSet<T>(desired);
        for (int index = collection.Count - 1; index >= 0; index--)
            if (!retained.Contains(collection[index])) collection.RemoveAt(index);

        for (int index = 0; index < desired.Count; index++)
        {
            if (index < collection.Count && EqualityComparer<T>.Default.Equals(collection[index], desired[index])) continue;
            int existing = index + 1;
            while (existing < collection.Count && !EqualityComparer<T>.Default.Equals(collection[existing], desired[index])) existing++;
            if (existing < collection.Count) collection.Move(existing, index);
            else collection.Insert(index, desired[index]);
        }
        while (collection.Count > desired.Count) collection.RemoveAt(collection.Count - 1);
    }
}
