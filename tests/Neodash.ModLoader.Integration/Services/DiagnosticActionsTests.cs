using Neodash_Mod_Loader.Services;
using Neodash_Mod_Loader.Models;
using Xunit;

namespace Neodash.ModLoader.Integration;

public sealed class DiagnosticActionsTests
{
    private static readonly string[] LogNames = ["crash", "operations", "ue4ss"];

    [Fact]
    public void BuildCommitIsExposedThroughDiagnostics()
    {
        const string hash = "0123456789abcdef0123456789abcdef01234567";
        var actions = new DiagnosticActions(new FakeDiagnostics { CommitHash = hash }, new FakeFiles(), new FakeDialogs(), new FakeClipboard());

        Assert.Equal(hash, actions.CommitHash);
        Assert.Equal(new Version(1, 4), actions.Version);
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef01234567")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public void CopyCommitCopiesFullHash(string hash)
    {
        var clipboard = new FakeClipboard();
        var actions = new DiagnosticActions(new FakeDiagnostics { CommitHash = hash }, new FakeFiles(), new FakeDialogs(), clipboard);

        Assert.True(actions.CopyCommit());
        Assert.Equal(hash, Assert.Single(clipboard.CopiedText));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void UnavailableCommitDoesNotChangeClipboard(string? hash)
    {
        var clipboard = new FakeClipboard();
        var actions = new DiagnosticActions(new FakeDiagnostics { CommitHash = hash }, new FakeFiles(), new FakeDialogs(), clipboard);

        Assert.False(actions.CopyCommit());
        Assert.Empty(clipboard.CopiedText);
    }

    [Fact]
    public void ClipboardFailureIsNotReportedAsSuccess()
    {
        var error = new InvalidOperationException("Clipboard is unavailable.");
        var clipboard = new FakeClipboard { CopyError = error };
        var actions = new DiagnosticActions(new FakeDiagnostics { CommitHash = "0123456789abcdef0123456789abcdef01234567" }, new FakeFiles(), new FakeDialogs(), clipboard);

        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => { actions.CopyCommit(); }));
        Assert.Empty(clipboard.CopiedText);
    }

    [Fact]
    public async Task MissingLogsAreUnavailable()
    {
        foreach (string name in LogNames)
        {
            var diagnostics = new FakeDiagnostics { Available = false };
            var files = new FakeFiles();
            var dialogs = new FakeDialogs();

            await new DiagnosticActions(diagnostics, files, dialogs, new FakeClipboard()).OpenAsync(name);

            Assert.Empty(files.OpenedPaths);
            Assert.Equal(0, diagnostics.LocationRequests);
            Assert.Equal("Log unavailable", Assert.Single(dialogs.Messages).Title);
            Assert.Empty(diagnostics.LoggedErrors);
        }
    }

    [Fact]
    public async Task LogsDisappearingDuringOpenAreUnavailable()
    {
        foreach (string name in LogNames)
        {
            foreach (Exception error in new Exception[] { new FileNotFoundException(), new DirectoryNotFoundException() })
            {
                var diagnostics = new FakeDiagnostics { Available = true };
                var files = new FakeFiles { OpenError = error };
                var dialogs = new FakeDialogs();

                await new DiagnosticActions(diagnostics, files, dialogs, new FakeClipboard()).OpenAsync(name);

                Assert.Equal(name + ".txt", Assert.Single(files.OpenedPaths));
                Assert.Equal("Log unavailable", Assert.Single(dialogs.Messages).Title);
                Assert.Empty(diagnostics.LoggedErrors);
            }
        }
    }

    [Fact]
    public async Task ExistingLogsOpenWithoutMessages()
    {
        foreach (string name in LogNames)
        {
            var diagnostics = new FakeDiagnostics { Available = true };
            var files = new FakeFiles();
            var dialogs = new FakeDialogs();

            await new DiagnosticActions(diagnostics, files, dialogs, new FakeClipboard()).OpenAsync(name);

            Assert.Equal(name + ".txt", Assert.Single(files.OpenedPaths));
            Assert.Empty(dialogs.Messages);
            Assert.Empty(diagnostics.LoggedErrors);
        }
    }

    [Fact]
    public async Task LogAccessErrorsAreNotHidden()
    {
        foreach (Exception error in new Exception[] { new UnauthorizedAccessException(), new IOException("Locked file") })
        {
            var diagnostics = new FakeDiagnostics { Available = true };
            var files = new FakeFiles { OpenError = error };
            var dialogs = new FakeDialogs();
            var actions = new DiagnosticActions(diagnostics, files, dialogs, new FakeClipboard());

            Assert.Same(error, await Assert.ThrowsAnyAsync<Exception>(() => actions.OpenAsync("crash")));
            Assert.Single(files.OpenedPaths);
            Assert.Empty(dialogs.Messages);
        }
    }

    [Fact]
    public async Task MissingNonLogLocationsKeepTheirErrors()
    {
        foreach (string name in new[] { "settings", "backups", "folder" })
        {
            foreach (Exception error in new Exception[] { new FileNotFoundException(), new DirectoryNotFoundException() })
            {
                var diagnostics = new FakeDiagnostics { Available = false };
                var files = new FakeFiles { OpenError = error };
                var dialogs = new FakeDialogs();
                var actions = new DiagnosticActions(diagnostics, files, dialogs, new FakeClipboard());

                Assert.Same(error, await Assert.ThrowsAnyAsync<Exception>(() => actions.OpenAsync(name)));
                Assert.Equal(name + ".txt", Assert.Single(files.OpenedPaths));
                Assert.Empty(dialogs.Messages);
            }
        }
    }

    private sealed class FakeClipboard : IDesktopClipboard
    {
        public List<string> CopiedText { get; } = [];
        public Exception? CopyError { get; init; }

        public void SetText(string text)
        {
            if (CopyError is not null) throw CopyError;
            CopiedText.Add(text);
        }
    }

    private sealed class FakeDiagnostics : IAppDiagnostics
    {
        public bool Available { get; init; }
        public int LocationRequests { get; private set; }
        public List<Exception?> LoggedErrors { get; } = [];
        public Version Version => new(1, 4);
        public string? CommitHash { get; init; }
        public bool CanOpenLocation(string name) => Available;
        public string GetLocation(string name)
        {
            LocationRequests++;
            return name + ".txt";
        }
        public void Log(string message, Exception? exception = null) => LoggedErrors.Add(exception);
        public void LogCrash(Exception exception) => throw new NotSupportedException();
    }

    private sealed class FakeFiles : IDesktopFiles
    {
        public Exception? OpenError { get; init; }
        public List<string> OpenedPaths { get; } = [];
        public void OpenPath(string path)
        {
            OpenedPaths.Add(path);
            if (OpenError is not null) throw OpenError;
        }
        public bool CanOpenInCode => false;
        public void OpenInCode(string folder) => throw new NotSupportedException();
        public Task<string?> PickFileAsync(params string[] extensions) => throw new NotSupportedException();
        public Task<string?> PickFolderAsync() => throw new NotSupportedException();
        public Task<string?> SaveFileAsync(string suggestedName, string extension) => throw new NotSupportedException();
    }

    private sealed class FakeDialogs : IUserDialogs
    {
        public List<(string Title, string Message)> Messages { get; } = [];
        public bool IsDialogOpen => false;
        public Task ShowMessageAsync(string title, string message)
        {
            Messages.Add((title, message));
            return Task.CompletedTask;
        }
        public Task<bool> ConfirmAsync(string title, string message, string primary = "Continue") => throw new NotSupportedException();
        public Task<UnsavedChangesChoice> AskUnsavedChangesAsync(string message) => throw new NotSupportedException();
        public Task<bool> RunAsync(string title, Func<IProgress<OperationProgress>, CancellationToken, Task> action, bool allowCancellation = true) => throw new NotSupportedException();
    }
}
