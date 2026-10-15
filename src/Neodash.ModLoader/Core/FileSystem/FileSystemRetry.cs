namespace Neodash_Mod_Loader;

/// <summary>Retries moves blocked briefly by Windows filesystem access or sharing locks.</summary>
internal static class FileSystemRetry
{
    private const int MaximumMoveAttempts = 4;

    internal static void Move(Action move, Action<TimeSpan> delay)
    {
        for (int attempt = 1; attempt <= MaximumMoveAttempts; attempt++)
        {
            try
            {
                move();
                return;
            }
            catch (Exception exception) when (attempt < MaximumMoveAttempts && IsTransientWindowsLock(exception))
            {
                delay(TimeSpan.FromMilliseconds(attempt * 20));
            }
        }
    }

    private static bool IsTransientWindowsLock(Exception exception)
    {
        if (!OperatingSystem.IsWindows() || exception is not (UnauthorizedAccessException or IOException)) return false;
        int errorCode = exception.HResult & 0xFFFF;
        return errorCode is 5 or 32 or 33;
    }
}
