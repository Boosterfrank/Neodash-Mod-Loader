using Neodash_Mod_Loader.Models;
using Neodash_Mod_Loader.Services;
using Xunit;

namespace Neodash.ModLoader.Integration;

public sealed class AppUpdateCoordinatorTests
{
    [Fact]
    public async Task CancellationAtDownloadCompletionDoesNotLaunchTheInstaller()
    {
        using var fixture = new Fixture();
        fixture.Updates.AfterDownload = fixture.Dialogs.Cancellation.Cancel;
        await fixture.Coordinator.CheckAsync();
        Assert.Equal(1, fixture.Updates.Downloads);
        Assert.Empty(fixture.Launcher.Opened);
        Assert.Empty(fixture.Dialogs.Messages);
    }

    [Fact]
    public async Task ManualCheckShowsProgressAndLaunchesOnlyAfterDownload()
    {
        using var fixture = new Fixture();
        await fixture.Coordinator.CheckAsync(showUpToDate: true);
        Assert.Equal(["Check for application updates", "Download application update"], fixture.Dialogs.Operations);
        Assert.Contains(fixture.Dialogs.Progress, value => value.Message == "Checking for updates…");
        Assert.Equal(["installer.exe"], fixture.Launcher.Opened);
        Assert.Equal(1, fixture.Updates.Downloads);
    }

    [Fact]
    public async Task CancelledCheckDoesNotClaimTheAppIsCurrentOrDownloadAnything()
    {
        using var fixture = new Fixture();
        fixture.Dialogs.CancelOperation = "Check for application updates";
        await fixture.Coordinator.CheckAsync(showUpToDate: true);
        Assert.Empty(fixture.Dialogs.Messages);
        Assert.Equal(0, fixture.Updates.Downloads);
        Assert.Empty(fixture.Launcher.Opened);
    }

    [Fact]
    public async Task AcceptedStartupUpdateReportsInstallerLaunchFailures()
    {
        using var fixture = new Fixture();
        fixture.Launcher.Error = new IOException("Installer could not be opened");
        await fixture.Coordinator.CheckAsync();
        var error = Assert.Single(fixture.Dialogs.Messages);
        Assert.Equal("Application update failed", error.Title);
        Assert.Contains("Installer could not be opened", error.Message);
        Assert.Single(fixture.Diagnostics.Errors);
    }

    [Fact]
    public async Task DownloadFailureAndCancellationNeverLaunchAnInstaller()
    {
        using var fixture = new Fixture();
        fixture.Updates.Error = new IOException("Offline");
        await fixture.Coordinator.CheckAsync();
        Assert.Contains(fixture.Dialogs.Messages, item => item.Message.Contains("Offline", StringComparison.Ordinal));
        Assert.Empty(fixture.Launcher.Opened);
        fixture.Updates.Error = null;
        fixture.Dialogs.CancelOperation = "Download application update";
        await fixture.Coordinator.CheckAsync();
        Assert.Empty(fixture.Launcher.Opened);
        Assert.Equal(1, fixture.Updates.Downloads);
    }

    [Fact]
    public async Task DecliningUpdateOrFailedPendingSavePreventsDownload()
    {
        using var fixture = new Fixture();
        fixture.Dialogs.Accept = false;
        await fixture.Coordinator.CheckAsync();
        Assert.Equal(0, fixture.Updates.Downloads);
        fixture.Dialogs.Accept = true;
        fixture.Edits.HasUnsavedChanges = true;
        fixture.Edits.SaveResult = false;
        await fixture.Coordinator.CheckAsync();
        Assert.Equal(1, fixture.Edits.Saves);
        Assert.Equal(0, fixture.Updates.Downloads);
        Assert.Empty(fixture.Launcher.Opened);
    }

    [Fact]
    public async Task CurrentVersionIsReportedOnlyForManualChecks()
    {
        using var fixture = new Fixture();
        fixture.Updates.Version = null;
        await fixture.Coordinator.CheckAsync();
        Assert.Empty(fixture.Dialogs.Messages);
        await fixture.Coordinator.CheckAsync(showUpToDate: true);
        Assert.Equal("No update available", Assert.Single(fixture.Dialogs.Messages).Title);
    }

    [Fact]
    public async Task ConcurrentChecksAreIgnoredAndDisposalCancelsTheRequest()
    {
        using var fixture = new Fixture();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Updates.Find = async token => { started.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); return null; };
        Task check = fixture.Coordinator.CheckAsync();
        await started.Task;
        await fixture.Coordinator.CheckAsync();
        Assert.Equal(1, fixture.Updates.Checks);
        fixture.Coordinator.Dispose();
        await check;
        await fixture.Coordinator.CheckAsync();
        Assert.Equal(1, fixture.Updates.Checks);
        Assert.Empty(fixture.Dialogs.Messages);
        Assert.Empty(fixture.Launcher.Opened);
    }

    private sealed class Fixture : IDisposable
    {
        public FakeUpdates Updates { get; } = new();
        public FakeDialogs Dialogs { get; } = new();
        public FakeLauncher Launcher { get; } = new();
        public FakeEdits Edits { get; } = new();
        public FakeDiagnostics Diagnostics { get; } = new();
        public AppUpdateCoordinator Coordinator { get; }
        public Fixture() => Coordinator = new(Updates, new FakeWorkspace(), Edits, Dialogs, Launcher, Diagnostics);
        public void Dispose()
        {
            Coordinator.Dispose();
            Dialogs.Dispose();
        }
    }

    private sealed class FakeUpdates : IApplicationUpdates
    {
        public Version? Version { get; set; } = new(1, 5);
        public Func<CancellationToken, Task<Version?>>? Find { get; set; }
        public Exception? Error { get; set; }
        public Action? AfterDownload { get; set; }
        public int Checks { get; private set; }
        public int Downloads { get; private set; }
        public Task<Version?> FindUpdateAsync(CancellationToken token = default) { Checks++; return Find?.Invoke(token) ?? Task.FromResult(Version); }
        public Task<string> DownloadAsync(IProgress<OperationProgress>? progress = null, CancellationToken token = default)
        {
            Downloads++;
            if (Error is not null) throw Error;
            AfterDownload?.Invoke();
            return Task.FromResult("installer.exe");
        }
    }

    private sealed class FakeDialogs : IUserDialogs, IDisposable
    {
        public CancellationTokenSource Cancellation { get; } = new();
        public bool IsDialogOpen => false;
        public bool Accept { get; set; } = true;
        public string? CancelOperation { get; set; }
        public List<string> Operations { get; } = [];
        public List<OperationProgress> Progress { get; } = [];
        public List<(string Title, string Message)> Messages { get; } = [];
        public Task ShowMessageAsync(string title, string message) { Messages.Add((title, message)); return Task.CompletedTask; }
        public Task<bool> ConfirmAsync(string title, string message, string primary = "Continue") => Task.FromResult(Accept);
        public Task<UnsavedChangesChoice> AskUnsavedChangesAsync(string message) => throw new NotSupportedException();
        public async Task<bool> RunAsync(string title, Func<IProgress<OperationProgress>, CancellationToken, Task> action, bool allowCancellation = true)
        {
            Operations.Add(title);
            if (CancelOperation == title) return false;
            try { await action(new InlineProgress(Progress.Add), Cancellation.Token); return !Cancellation.IsCancellationRequested; }
            catch (OperationCanceledException) when (Cancellation.IsCancellationRequested) { return false; }
            catch (Exception ex) { await ShowMessageAsync(title + " failed", ex.Message); return false; }
        }
        public void Dispose() => Cancellation.Dispose();
    }

    private sealed class InlineProgress(Action<OperationProgress> action) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => action(value);
    }

    private sealed class FakeLauncher : IInstallerLauncher
    {
        public Exception? Error { get; set; }
        public List<string> Opened { get; } = [];
        public void OpenInstaller(string path)
        {
            if (Error is not null) throw Error;
            Opened.Add(path);
        }
    }

    private sealed class FakeEdits : IModEditSession
    {
        public bool HasUnsavedChanges { get; set; }
        public bool SaveResult { get; set; } = true;
        public int Saves { get; private set; }
        public IReadOnlyList<EditableMod> Mods => [];
        public event EventHandler? Changed { add { /* No background notifications in this fake. */ } remove { /* No subscriptions to retain. */ } }
        public void Reload() => throw new NotSupportedException();
        public void Discard() => throw new NotSupportedException();
        public Task<bool> SaveAsync() { Saves++; return Task.FromResult(SaveResult); }
    }

    private sealed class FakeDiagnostics : IAppDiagnostics
    {
        public Version Version => new(1, 4);
        public string? CommitHash => null;
        public List<Exception?> Errors { get; } = [];
        public void Log(string message, Exception? exception = null) => Errors.Add(exception);
        public void LogCrash(Exception exception) => throw new NotSupportedException();
        public string GetLocation(string name) => throw new NotSupportedException();
        public bool CanOpenLocation(string name) => throw new NotSupportedException();
    }

    private sealed class FakeWorkspace : IModWorkspace
    {
        public bool IsBusy => false;
        public event EventHandler? Changed { add { /* No workspace events for update tests. */ } remove { /* No subscriptions to retain. */ } }
        public IReadOnlyList<InstalledModState> GetInstalledMods() => throw new NotSupportedException();
        public string GetModFolder(string name) => throw new NotSupportedException();
        public string? GetLuaScriptFolder(string name) => throw new NotSupportedException();
        public int MaxLuaModNameLength => 64;
        public string? ValidateLuaModName(string? name) => throw new NotSupportedException();
        public string GetEntryPoint(string name, bool enabled) => throw new NotSupportedException();
        public IReadOnlyList<string> GetOwnedFolders(string name) => throw new NotSupportedException();
        public void ValidateEdits() => throw new NotSupportedException();
        public void ApplyChanges(IReadOnlyDictionary<string, bool> changes) => throw new NotSupportedException();
        public Task ImportZipAsync(string path, IProgress<OperationProgress>? progress = null, CancellationToken token = default) => throw new NotSupportedException();
        public Task ImportFolderAsync(string path, IProgress<OperationProgress>? progress = null, CancellationToken token = default) => throw new NotSupportedException();
        public Task CreateLuaModAsync(string name, IProgress<OperationProgress>? progress = null, CancellationToken token = default) => throw new NotSupportedException();
        public Task UninstallAsync(string name) => throw new NotSupportedException();
        public Task RepairAsync(string name, IProgress<OperationProgress>? progress = null, CancellationToken token = default) => throw new NotSupportedException();
        public Task RestoreAsync(string name) => throw new NotSupportedException();
    }
}
