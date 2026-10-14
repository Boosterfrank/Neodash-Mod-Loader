using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Neodash_Mod_Loader.Services;
using Neodash_Mod_Loader.Ui;
using Xunit;

namespace Neodash.ModLoader.Integration;

public sealed class ObservableCollectionSyncTests
{
    [Fact]
    public void CheckingModsLeavesRenderedRowsIntact()
    {
        var first = new EditableMod(new("First", false, "Scripts/main.lua", false, false, string.Empty));
        var second = new EditableMod(new("Second", true, "dlls/main.dll", false, false, string.Empty));
        ObservableCollection<EditableMod> rendered = [first, second];
        List<NotifyCollectionChangedEventArgs> events = [];
        rendered.CollectionChanged += (_, change) => events.Add(change);

        first.IsEnabled = true;
        ObservableCollectionSync.Update(rendered, new[] { first, second });
        first.IsEnabled = false;
        ObservableCollectionSync.Update(rendered, new[] { first, second });

        Assert.Empty(events);
        Assert.Same(first, rendered[0]);
        Assert.Same(second, rendered[1]);
        Assert.False(first.IsEnabled);
    }

    [Fact]
    public void EnabledFilterRemovesOnlyTheToggledRow()
    {
        var first = new EditableMod(new("First", true, string.Empty, false, false, string.Empty));
        var second = new EditableMod(new("Second", true, string.Empty, false, false, string.Empty));
        var third = new EditableMod(new("Third", true, string.Empty, false, false, string.Empty));
        EditableMod[] all = [first, second, third];
        ObservableCollection<EditableMod> rendered = [.. all];
        List<NotifyCollectionChangedEventArgs> events = [];
        rendered.CollectionChanged += (_, change) => events.Add(change);

        second.IsEnabled = false;
        ObservableCollectionSync.Update(rendered, all.Where(mod => mod.IsEnabled));

        var removal = Assert.Single(events);
        Assert.Equal(NotifyCollectionChangedAction.Remove, removal.Action);
        Assert.Equal(1, removal.OldStartingIndex);
        Assert.Same(second, Assert.Single(removal.OldItems!.Cast<EditableMod>()));
        Assert.Same(first, rendered[0]);
        Assert.Same(third, rendered[1]);
    }

    [Theory]
    [InlineData("ABC", "CAB")]
    [InlineData("ABC", "BDA")]
    [InlineData("", "AB")]
    [InlineData("AB", "")]
    [InlineData("AAB", "BAA")]
    [InlineData("AAB", "ABAAB")]
    public void MembershipAndOrderChangesAvoidCollectionReset(string original, string desired)
    {
        ObservableCollection<char> rendered = [.. original];
        List<NotifyCollectionChangedAction> actions = [];
        rendered.CollectionChanged += (_, change) => actions.Add(change.Action);

        ObservableCollectionSync.Update(rendered, desired);

        Assert.Equal(desired.ToCharArray(), rendered.ToArray());
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);
    }

    [Fact]
    public void ExistingCollectionCanBeUsedAsTheSourceWithoutChanges()
    {
        ObservableCollection<string> rendered = ["First", "Second"];
        int notifications = 0;
        rendered.CollectionChanged += (_, _) => notifications++;

        ObservableCollectionSync.Update(rendered, rendered);

        Assert.Equal(0, notifications);
        Assert.Equal(["First", "Second"], rendered);
    }
}
