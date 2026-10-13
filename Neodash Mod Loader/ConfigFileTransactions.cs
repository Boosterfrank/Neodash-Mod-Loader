namespace Neodash_Mod_Loader;

public sealed record ConfigFileChange(ConfigFileDocument Document, string Text);

/// <summary>Validates every configuration edit before atomically applying the set.</summary>
public static class ConfigFileTransactions
{
    public static void Apply(IEnumerable<ConfigFileChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var prepared = new List<(ConfigFileDocument Document, byte[] Bytes)>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (ConfigFileChange change in changes)
        {
            ArgumentNullException.ThrowIfNull(change);
            if (!paths.Add(change.Document.Path)) throw new IOException("The same configuration was selected more than once.");
            if (string.Equals(change.Text, change.Document.Text, StringComparison.Ordinal)) continue;

            byte[] current = File.ReadAllBytes(change.Document.Path);
            if (!current.AsSpan().SequenceEqual(change.Document.OriginalBytes))
                throw new IOException("A configuration changed outside the loader while it was being edited. Nothing was overwritten: " + Path.GetFileName(change.Document.Path));

            // Encode every changed document before the first write, so a non-representable edit cannot leave a partial save.
            prepared.Add((change.Document, change.Document.Encode(change.Text)));
        }

        var written = new List<ConfigFileDocument>();
        try
        {
            foreach ((ConfigFileDocument document, byte[] bytes) in prepared)
            {
                AppStorage.WriteAtomically(document.Path, bytes);
                written.Add(document);
            }
        }
        catch (Exception failure)
        {
            var errors = new List<Exception> { failure };
            foreach (ConfigFileDocument document in written)
            {
                try { AppStorage.WriteAtomically(document.Path, document.OriginalBytes); }
                catch (Exception exception) { errors.Add(exception); }
            }

            if (errors.Count > 1)
                throw new AggregateException("Configuration save failed and some original file bytes could not be restored.", errors);
            throw;
        }
    }
}
