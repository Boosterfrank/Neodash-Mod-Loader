using static Neodash.ModLoader.Integration.TestFixtures;
using System.Diagnostics;
using System.Reflection;
using Neodash_Mod_Loader;

namespace Neodash.ModLoader.Integration;

internal static class IntegrationTestHost
{
    private const string ChildEnvironmentVariable = "NEODASH_XUNIT_CHILD";
    internal const string CorruptProfileChildVariable = "NEODASH_PROFILE_CORRUPTION_CHILD";

    private static bool IsChildProcess() => string.Equals(Environment.GetEnvironmentVariable(ChildEnvironmentVariable), "1", StringComparison.Ordinal);
    internal static bool IsCorruptProfileChild() => string.Equals(Environment.GetEnvironmentVariable(CorruptProfileChildVariable), "1", StringComparison.Ordinal);
    internal static bool IsLegacyMigrationChild() => string.Equals(Environment.GetEnvironmentVariable("NEODASH_LEGACY_MIGRATION_CHILD"), "1", StringComparison.Ordinal);

    // The parent asserts the child host's result; the child runs the scenario's assertions.
    internal static async Task AssertIsolatedAsync(string method, Func<string, Task> test)
    {
        if (!IsChildProcess())
        {
            await RunFilteredChildAsync(method, null);
            return;
        }

        string scratch = CreateScratch();
        try
        {
            Environment.SetEnvironmentVariable("NEODASH_TEST_STORAGE", Path.Combine(scratch, "service-storage"));
            Directory.CreateDirectory(AppStorage.DirectoryPath);
            ResetTestDoubles();
            await test(scratch);
        }
        finally
        {
            ResetTestDoubles();
            DeleteScratch(scratch);
        }
    }

    internal static async Task RunFilteredChildAsync(string method, Action<ProcessStartInfo>? configure)
    {
        string executable = Path.Combine(AppContext.BaseDirectory, Assembly.GetExecutingAssembly().GetName().Name + ".exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("The xUnit MTP test executable was not found.", executable);
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("--filter-method");
        start.ArgumentList.Add(typeof(IntegrationTests).FullName + "." + method);
        start.Environment[ChildEnvironmentVariable] = "1";
        configure?.Invoke(start);
        using Process child = Process.Start(start) ?? throw new InvalidOperationException("Could not start the isolated xUnit test host.");
        Task<string> standardOutputTask = child.StandardOutput.ReadToEndAsync();
        Task<string> standardErrorTask = child.StandardError.ReadToEndAsync();
        await child.WaitForExitAsync();
        await Task.WhenAll(standardOutputTask, standardErrorTask);
        string standardOutput = standardOutputTask.Result;
        string standardError = standardErrorTask.Result;
        Assert(child.ExitCode == 0, $"isolated xUnit test '{method}' failed with exit code {child.ExitCode}.{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
    }

    private static void ResetTestDoubles()
    {
        GameRuntime.RunningProbe = null;
        AppStorage.FailSettingsWriteNumber = null;
        AppStorage.FailWritePath = null;
    }
}
