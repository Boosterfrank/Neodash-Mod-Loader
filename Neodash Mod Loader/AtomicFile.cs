namespace Neodash_Mod_Loader;

/// <summary>Writes a complete replacement beside its destination, then swaps it in atomically.</summary>
internal static class AtomicFile
{
    internal static void Write(string path, byte[] content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);

        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath) ?? throw new IOException("Invalid file path.");
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, content);
            FileSystemRetry.Move(() => File.Move(temporary, fullPath, overwrite: true), Thread.Sleep);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            catch (IOException) { /* The original write or move error is more useful than cleanup failure. */ }
            catch (UnauthorizedAccessException) { /* The original write or move error is more useful than cleanup failure. */ }
        }
    }

}
