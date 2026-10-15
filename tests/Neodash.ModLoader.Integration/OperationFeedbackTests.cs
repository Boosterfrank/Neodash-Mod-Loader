using Neodash_Mod_Loader.Models;
using Neodash_Mod_Loader.Services;
using Xunit;

namespace Neodash.ModLoader.Integration;

public sealed class OperationFeedbackTests
{
    [Fact]
    public void QueuedProgressCoalescesAndDropsReportsAfterCancellation()
    {
        var dispatcher = new QueuedDispatcher();
        List<int> rendered = [];
        using var progress = new DispatchedProgress<int>(dispatcher, rendered.Add);
        progress.Report(1);
        progress.Report(2);
        Assert.Single(dispatcher.Pending);
        Assert.Empty(rendered);
        dispatcher.Drain();
        Assert.Equal([2], rendered);
        progress.Report(3);
        progress.Dispose();
        progress.Report(4);
        dispatcher.Drain();
        Assert.Equal([2], rendered);
    }

    [Fact]
    public void DownloadPresentationShowsSizeSpeedAndApproximateEta()
    {
        var display = OperationProgressPresentation.From(new("Downloading", 25, 1_048_576, 4_194_304, 1_048_576, TimeSpan.FromSeconds(3)));
        Assert.Equal(25, display.Percent);
        Assert.Contains("MiB of", display.Detail);
        Assert.Contains("MiB/s", display.Detail);
        Assert.Equal("About 3 sec remaining", display.Remaining);
    }

    [Fact]
    public void UnknownLengthRemainsIndeterminateWithoutInventingEta()
    {
        var display = OperationProgressPresentation.From(new("Downloading", BytesReceived: 4096, BytesPerSecond: 1024));
        Assert.Null(display.Percent);
        Assert.Equal("Time remaining unavailable", display.Remaining);
        Assert.DoesNotContain(" of ", display.Detail);
        Assert.Contains("KiB/s", display.Detail);
    }

    [Fact]
    public void ProgressPresentationHandlesStartingCompletedAndNonFiniteValues()
    {
        Assert.Equal("Estimating time remaining…", OperationProgressPresentation.From(new("Downloading", 0, 0, 4096)).Remaining);
        Assert.Equal("Download complete", OperationProgressPresentation.From(new("Downloading", 100, 4096, 4096)).Remaining);
        var invalid = OperationProgressPresentation.From(new("Working", double.NaN, 4, null, double.PositiveInfinity));
        Assert.Null(invalid.Percent);
        Assert.DoesNotContain("Infinity", invalid.Detail);
        Assert.Equal(string.Empty, OperationProgressPresentation.From(new("Installing")).Detail);
    }

    [Fact]
    public void ItemProgressLabelsTheCurrentItemAndMapsOnlyKnownPercentages()
    {
        List<OperationProgress> reports = [];
        var progress = new ItemOperationProgress(new InlineProgress(reports.Add), "Example", 1, 4);
        progress.Report(new("Downloading", 50, 100, 200));
        progress.Report(new("Installing"));
        Assert.Equal(37.5, reports[0].Percent);
        Assert.Contains("2 of 4: Example", reports[0].Message);
        Assert.Equal(100, reports[0].BytesReceived);
        Assert.Null(reports[1].Percent);
        Assert.Contains("Installing", reports[1].Message);
        Assert.Equal("Download complete", OperationProgressPresentation.From(new("Download complete", 50, 200, 200)).Remaining);
    }

    [Fact]
    public void NotificationsAreDispatchedAndUnsubscribedListenersAreNotCalled()
    {
        var dispatcher = new QueuedDispatcher();
        var notifications = new UserNotifications(dispatcher);
        List<UserNotification> received = [];
        void Handle(object? sender, UserNotification value) => received.Add(value);
        notifications.Published += Handle;
        notifications.Show("Saved", "Configuration saved");
        Assert.Empty(received);
        dispatcher.Drain();
        Assert.Equal(new UserNotification("Saved", "Configuration saved"), Assert.Single(received));
        notifications.Show("Later", "Closed window");
        notifications.Published -= Handle;
        dispatcher.Drain();
        Assert.Single(received);
    }

    private sealed class QueuedDispatcher : IUiDispatcher
    {
        public Queue<Action> Pending { get; } = [];
        public void Post(Action action) => Pending.Enqueue(action);
        public void Drain() { while (Pending.TryDequeue(out var action)) action(); }
    }

    private sealed class InlineProgress(Action<OperationProgress> report) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => report(value);
    }
}
