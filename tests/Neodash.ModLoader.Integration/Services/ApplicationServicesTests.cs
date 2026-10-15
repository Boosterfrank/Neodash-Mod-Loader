using Microsoft.Extensions.DependencyInjection;
using Neodash_Mod_Loader.Models;
using Neodash_Mod_Loader.Services;
using Xunit;

namespace Neodash.ModLoader.Integration;

public sealed class ApplicationServicesTests
{
    [Fact]
    public async Task BusyGuardExplainsBlockedActionsAndAllowsIdleWork()
    {
        var (session, workspace, _, dialogs) = NewSession();
        var guard = new NavigationGuard(session, workspace, dialogs);
        Assert.True(await guard.EnsureNotBusyAsync());
        Assert.Empty(dialogs.Messages);

        workspace.IsBusyValue = true;
        Assert.False(await guard.EnsureNotBusyAsync());
        Assert.Equal("Another mod operation is running", Assert.Single(dialogs.Messages));
        Assert.Equal(0, workspace.ApplyCount);
    }

    [Fact]
    public async Task SaveWithNoChangesDoesNotWrite()
    {
        var (session, workspace, _, _) = NewSession();
        session.Reload();
        Assert.True(await session.SaveAsync());
        Assert.Equal(0, workspace.ApplyCount);
    }

    [Fact]
    public async Task SuccessfulSaveUpdatesOriginalStateAndEntryPoint()
    {
        var (session, workspace, _, _) = NewSession();
        session.Reload();
        session.Mods[0].IsEnabled = false;
        Assert.True(await session.SaveAsync());
        Assert.Equal(1, workspace.ApplyCount);
        Assert.False(session.HasUnsavedChanges);
        Assert.False(session.Mods[0].OriginalIsEnabled);
        Assert.Equal("updated", session.Mods[0].EntryPointPath);
        var applied = Assert.Single(workspace.AppliedChanges);
        Assert.Equal("Test", applied.Key);
        Assert.False(applied.Value);
    }

    [Fact]
    public async Task FailedApplyRetainsDraftAndOriginalState()
    {
        var (session, workspace, _, _) = NewSession();
        workspace.ApplyFailure = new IOException("nope");
        session.Reload();
        session.Mods[0].IsEnabled = false;
        Assert.False(await session.SaveAsync());
        Assert.True(session.HasUnsavedChanges);
        Assert.True(session.Mods[0].OriginalIsEnabled);
        Assert.Equal(1, workspace.ApplyCount);
    }

    [Fact]
    public async Task BusyRunningAndPreflightFailuresDoNotWrite()
    {
        var (session, workspace, game, _) = NewSession();
        session.Reload();
        session.Mods[0].IsEnabled = false;
        workspace.IsBusyValue = true;
        Assert.False(await session.SaveAsync());
        workspace.IsBusyValue = false;
        game.CloseFailure = new InvalidOperationException();
        Assert.False(await session.SaveAsync());
        game.CloseFailure = null;
        workspace.ValidationFailure = new InvalidOperationException();
        Assert.False(await session.SaveAsync());
        Assert.Equal(0, workspace.ApplyCount);
        Assert.True(session.HasUnsavedChanges);
    }

    [Fact]
    public void DiscardRestoresDraftWithoutWorkspaceIo()
    {
        var (session, workspace, _, _) = NewSession();
        session.Reload();
        session.Mods[0].IsEnabled = false;
        session.Discard();
        Assert.True(session.Mods[0].IsEnabled);
        Assert.False(session.HasUnsavedChanges);
        Assert.Equal(0, workspace.ApplyCount);
    }

    [Fact]
    public void ReloadFailureClearsStaleAndPartialMods()
    {
        var (session, workspace, _, _) = NewSession();
        session.Reload();
        workspace.LoadFailure = new IOException();
        Assert.Throws<IOException>(() => session.Reload());
        Assert.Empty(session.Mods);
    }

    [Fact]
    public void DisposeUnsubscribesEditableModNotifications()
    {
        var (session, _, _, _) = NewSession();
        session.Reload();
        int changes = 0;
        session.Changed += (_, _) => changes++;
        EditableMod mod = session.Mods[0];
        session.Dispose();
        mod.IsEnabled = false;
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task NavigationGuardHonorsSaveDiscardCancelAndBlockedState()
    {
        var (session, workspace, _, dialogs) = NewSession();
        session.Reload();
        session.Mods[0].IsEnabled = false;
        var guard = new NavigationGuard(session, workspace, dialogs);
        dialogs.Choice = UnsavedChangesChoice.Save;
        Assert.True(await guard.ResolveUnsavedChangesAsync("save"));
        Assert.False(session.HasUnsavedChanges);
        session.Mods[0].IsEnabled = true;
        dialogs.Choice = UnsavedChangesChoice.Discard;
        Assert.True(await guard.ResolveUnsavedChangesAsync("discard"));
        Assert.False(session.HasUnsavedChanges);
        session.Mods[0].IsEnabled = true;
        dialogs.Choice = UnsavedChangesChoice.Cancel;
        Assert.False(await guard.ResolveUnsavedChangesAsync("cancel"));
        Assert.True(session.HasUnsavedChanges);
        workspace.IsBusyValue = true;
        Assert.False(await guard.ResolveUnsavedChangesAsync("blocked"));
        Assert.True(session.HasUnsavedChanges);
    }

    [Fact]
    public async Task NavigationGuardSaveFailureDoesNotLoseChanges()
    {
        var (session, workspace, _, dialogs) = NewSession();
        session.Reload();
        session.Mods[0].IsEnabled = false;
        workspace.ApplyFailure = new IOException();
        dialogs.Choice = UnsavedChangesChoice.Save;
        Assert.False(await new NavigationGuard(session, workspace, dialogs).ResolveUnsavedChangesAsync("save"));
        Assert.True(session.HasUnsavedChanges);
    }

    [Fact]
    public void StartupCorruptSettingsBlocksAutoCompletionAndExistingRuntimeCompletesSetup()
    {
        var settings = new FakeSettings
        {
            LoadErrorValue = "corrupt"
        };
        var game = new FakeGame
        {
            Path = "game"
        };
        var runtime = new FakeRuntime
        {
            Installed = true
        };
        Assert.Equal(AppPage.Settings, new StartupCoordinator(settings, game, runtime, new FakeDiagnostics()).GetInitialPage());
        Assert.Equal(0, settings.SetupWrites);
        settings.LoadErrorValue = null;
        Assert.Equal(AppPage.Shell, new StartupCoordinator(settings, game, runtime, new FakeDiagnostics()).GetInitialPage());
        Assert.Equal(1, settings.SetupWrites);
    }

    [Fact]
    public void StartupSaveFailureKeepsOnboarding()
    {
        var settings = new FakeSettings
        {
            SetupFailure = new IOException()
        };
        var diagnostics = new FakeDiagnostics();
        Assert.Equal(AppPage.Onboarding, new StartupCoordinator(settings, new FakeGame { Path = "game" }, new FakeRuntime { Installed = true }, diagnostics).GetInitialPage());
        Assert.Equal(1, diagnostics.LogCount);
    }

    [Fact]
    public void CoreRegistrationBuildsWithFakeableOverridesAndScopedGuardrails()
    {
        var services = new ServiceCollection();
        services.AddModLoaderCore();
        services.AddSingleton<IAppDiagnostics, FakeDiagnostics>();
        services.AddSingleton<IGameService, FakeGame>();
        services.AddSingleton<ISettingsService, FakeSettings>();
        services.AddSingleton<IRuntimeService, FakeRuntime>();
        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Assert.IsType<FakeGame>(provider.GetRequiredService<IGameService>());
        Assert.IsType<StartupCoordinator>(provider.GetRequiredService<StartupCoordinator>());
    }

    [Fact]
    public void ScopedDraftsAreDistinctWhileOverriddenBackendIsShared()
    {
        var services = new ServiceCollection();
        services.AddModLoaderCore();
        services.AddSingleton<IModWorkspace, FakeWorkspace>();
        services.AddSingleton<IGameService, FakeGame>();
        services.AddSingleton<IAppDiagnostics, FakeDiagnostics>();
        services.AddScoped<FakeDialogs>();
        services.AddScoped<IUserDialogs>(provider => provider.GetRequiredService<FakeDialogs>());
        services.AddScoped<IModEditSession, ModEditSession>();
        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IModEditSession>());
        using IServiceScope first = provider.CreateScope();
        using IServiceScope second = provider.CreateScope();
        IModEditSession firstSession = first.ServiceProvider.GetRequiredService<IModEditSession>();
        IModEditSession secondSession = second.ServiceProvider.GetRequiredService<IModEditSession>();
        firstSession.Reload();
        secondSession.Reload();
        firstSession.Mods[0].IsEnabled = false;
        Assert.NotSame(firstSession, secondSession);
        Assert.True(firstSession.HasUnsavedChanges);
        Assert.False(secondSession.HasUnsavedChanges);
        Assert.NotSame(first.ServiceProvider.GetRequiredService<FakeDialogs>(), second.ServiceProvider.GetRequiredService<FakeDialogs>());
        Assert.Same(first.ServiceProvider.GetRequiredService<IModWorkspace>(), second.ServiceProvider.GetRequiredService<IModWorkspace>());
    }

    private static (ModEditSession Session, FakeWorkspace Workspace, FakeGame Game, FakeDialogs Dialogs) NewSession()
    {
        var workspace = new FakeWorkspace();
        var game = new FakeGame();
        var dialogs = new FakeDialogs();
        return (new ModEditSession(workspace, game, dialogs, new FakeDiagnostics()), workspace, game, dialogs);
    }

    private sealed class FakeWorkspace : IModWorkspace
    {
        public bool IsBusyValue;
        public bool IsBusy => IsBusyValue;

        public int ApplyCount;
        public Dictionary<string, bool> AppliedChanges { get; private set; } = [];
        public Exception? ApplyFailure;
        public Exception? ValidationFailure;
        public Exception? LoadFailure;
        public event EventHandler? Changed
        {
            add
            { /* The fake has no asynchronous workspace source to raise. */
            }

            remove
            { /* The fake has no asynchronous workspace source to raise. */
            }
        }

        public IReadOnlyList<InstalledModState> GetInstalledMods()
        {
            if (LoadFailure is not null)
                throw LoadFailure;
            return [new("Test", true, "original", false, false, "")];
        }

        public string GetModFolder(string name) => throw new NotSupportedException();
        public string GetEntryPoint(string name, bool enabled) => "updated";
        public IReadOnlyList<string> GetOwnedFolders(string name) => throw new NotSupportedException();
        public void ValidateEdits()
        {
            if (ValidationFailure is not null)
                throw ValidationFailure;
        }

        public void ApplyChanges(IReadOnlyDictionary<string, bool> changes)
        {
            ApplyCount++;
            if (ApplyFailure is not null)
                throw ApplyFailure;
            AppliedChanges = new Dictionary<string, bool>(changes);
        }

        public Task ImportZipAsync(string path, IProgress<OperationProgress>? progress = null, CancellationToken token = default) => throw new NotSupportedException();
        public Task ImportFolderAsync(string path, IProgress<OperationProgress>? progress = null, CancellationToken token = default) => throw new NotSupportedException();
        public int MaxLuaModNameLength => 64;
        public string? ValidateLuaModName(string? name) => throw new NotSupportedException();
        public string? GetLuaScriptFolder(string name) => throw new NotSupportedException();
        public Task CreateLuaModAsync(string name, IProgress<OperationProgress>? progress = null, CancellationToken token = default) => throw new NotSupportedException();
        public Task UninstallAsync(string name) => throw new NotSupportedException();
        public Task RepairAsync(string name, IProgress<OperationProgress>? progress = null, CancellationToken token = default) => throw new NotSupportedException();
        public Task RestoreAsync(string name) => throw new NotSupportedException();
    }

    private sealed class FakeGame : IGameService
    {
        public string Path = "game";
        public Exception? CloseFailure;
        public string GetPath() => Path;
        public string RequirePath() => Path;
        public bool IsRunning(string path) => false;
        public void EnsureClosed(string? gamePath = null)
        {
            if (CloseFailure is not null)
                throw CloseFailure;
        }

        public void Launch() => throw new NotSupportedException();
    }

    private sealed class FakeDialogs : IUserDialogs
    {
        public UnsavedChangesChoice Choice;
        public bool IsDialogOpen { get; set; }
        public List<string> Messages { get; } = [];

        public Task ShowMessageAsync(string title, string message)
        {
            Messages.Add(title);
            return Task.CompletedTask;
        }

        public Task<bool> ConfirmAsync(string title, string message, string primary = "Continue") => throw new NotSupportedException();
        public Task<UnsavedChangesChoice> AskUnsavedChangesAsync(string message) => Task.FromResult(Choice);
        public Task<bool> RunAsync(string title, Func<IProgress<OperationProgress>, CancellationToken, Task> action, bool allowCancellation = true) => throw new NotSupportedException();
    }

    private sealed class FakeDiagnostics : IAppDiagnostics
    {
        public int LogCount;
        public Version Version => new(1, 0);
        public string? CommitHash => null;

        public void Log(string message, Exception? exception = null) => LogCount++;
        public void LogCrash(Exception exception) => throw new NotSupportedException();
        public string GetLocation(string name) => throw new NotSupportedException();
        public bool CanOpenLocation(string name) => throw new NotSupportedException();
    }

    private sealed class FakeSettings : ISettingsService
    {
        public string? LoadErrorValue;
        public Exception? SetupFailure;
        public int SetupWrites;
        public string? Theme => "Dark";
        public bool IsSetupComplete { get; private set; }
        public string? LoadError => LoadErrorValue;

        public void Reload() => throw new NotSupportedException();

        public void SetTheme(string theme) => throw new NotSupportedException();

        public void SetSetupComplete(bool complete)
        {
            SetupWrites++;
            if (SetupFailure is not null)
                throw SetupFailure;
            IsSetupComplete = complete;
        }

        public void ChooseGameFolder(string selectedFolder, bool ensureGameClosed) => throw new NotSupportedException();

        public void DetectGame() => throw new NotSupportedException();
    }

    private sealed class FakeRuntime : IRuntimeService
    {
        public bool Installed;
        public bool IsInstalled(string gamePath) => Installed;
        public bool CanRemove(string gamePath) => throw new NotSupportedException();
        public string GetStatus(string gamePath) => "";
        public bool HasConfiguration => false;

        public ConfigurationLocation GetConfiguration() => new("", "", []);
        public Task InstallAsync(IProgress<OperationProgress>? progress = null, CancellationToken token = default) => throw new NotSupportedException();
        public Task UninstallAsync() => throw new NotSupportedException();
    }
}
