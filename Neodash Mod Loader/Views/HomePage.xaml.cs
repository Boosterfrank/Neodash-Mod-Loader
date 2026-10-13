using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Neodash_Mod_Loader.Services;
using System.Collections.ObjectModel;
using System.Numerics;
using Windows.UI.ViewManagement;

namespace Neodash_Mod_Loader.Views;

public sealed partial class HomePage : Page
{
    private const string TranslationProperty = "Translation";
    private readonly UISettings _uiSettings = new();
    private HomePageController? _controller;
    private bool _isUpdatingProfiles;
    private bool _subscriptionsActive;
    private int _previousModTabIndex = -1;
    private Visual? _animatedTabVisual;
    public ObservableCollection<ModItem> Mods { get; } = [];
    public ObservableCollection<ModItem> FilteredMods { get; } = [];
    public bool HasUnsavedChanges => _controller?.HasUnsavedChanges == true;

    public HomePage()
    {
        InitializeComponent();
        InstalledModsList.DragOver += ModList_DragOver;
        Loaded += HomePage_Loaded;
        Unloaded += HomePage_Unloaded;
    }

    internal void Activate(HomePageController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        if (_controller is not null && !ReferenceEquals(_controller, controller))
            throw new InvalidOperationException("HomePage has already been activated for this window.");
        _controller = controller;
        if (IsLoaded)
            StartController();
    }

    public async Task<bool> SaveModsAsync() => _controller is not null && await _controller.SaveAsync();
    public void DiscardChanges() => _controller?.Discard();
    private void HomePage_Loaded(object sender, RoutedEventArgs e) => StartController();
    private void HomePage_Unloaded(object sender, RoutedEventArgs e)
    {
        StopTabAnimation();
        _previousModTabIndex = -1;
        if (!_subscriptionsActive)
            return;
        _controller!.Changed -= Controller_Changed;
        _controller.Reloaded -= Controller_Reloaded;
        _controller.Deactivate();
        _subscriptionsActive = false;
        ClearRenderedMods();
    }

    private void StartController()
    {
        if (_controller is null || _subscriptionsActive)
            return;
        _controller.Changed += Controller_Changed;
        _controller.Reloaded += Controller_Reloaded;
        _subscriptionsActive = true;
        _controller.Initialize();
        RenderControllerState();
    }

    private void Controller_Changed(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_subscriptionsActive)
            UpdateControllerState();
    });
    private void Controller_Reloaded(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_subscriptionsActive)
            RenderControllerState();
    });
    private void UpdateControllerState()
    {
        RefreshProfileList();
        CheckDirtyState();
        ApplyFilter();
    }

    private void RenderControllerState()
    {
        if (_controller is null)
            return;
        ClearRenderedMods();
        foreach (EditableMod mod in _controller.Mods)
            Mods.Add(new ModItem(mod));
        RefreshProfileList();
        CheckDirtyState();
        ApplyFilter();
    }

    private void ClearRenderedMods()
    {
        FilteredMods.Clear();
        foreach (ModItem mod in Mods) mod.Dispose();
        Mods.Clear();
    }

    private void CheckDirtyState()
    {
        bool idle = _controller is { IsBusy: false };
        bool hasGame = _controller?.HasGame == true;
        SaveButton.IsEnabled = HasUnsavedChanges && idle && hasGame;
        InstallModButton.IsEnabled = idle && hasGame;
        LaunchButton.IsEnabled = idle && hasGame;
        FolderButton.IsEnabled = hasGame;
        NewProfileItem.IsEnabled = idle && hasGame;
        RefreshButton.IsEnabled = idle && hasGame;
        ProfileComboBox.IsEnabled = idle && hasGame;
        ProfileActionsButton.IsEnabled = idle;
        DeleteProfileButton.IsEnabled = idle && hasGame && (_controller?.Profiles.Count ?? 0) > 1;
    }
    public void ApplyFilter()
    {
        if (NoModsText is null)
            return;
        string search = ModSearchBox?.Text.Trim() ?? string.Empty;
        int filter = ModFilterComboBox?.SelectedIndex ?? 0;
        IEnumerable<ModItem> items = Mods;
        if (search.Length > 0)
            items = items.Where(mod => mod.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || mod.EntryPointPath.Contains(search, StringComparison.OrdinalIgnoreCase));
        items = filter switch
        {
            1 => items.Where(mod => mod.IsEnabled),
            2 => items.Where(mod => !mod.IsEnabled),
            _ => items
        };
        FilteredMods.Clear();
        foreach (ModItem mod in items)
            FilteredMods.Add(mod);
        bool canEdit = _controller is { IsBusy: false, LoadError: null } && _controller.HasGame;
        BulkTools.IsEnabled = canEdit && FilteredMods.Count > 0;
        EnableAllShownItem.IsEnabled = canEdit && FilteredMods.Any(mod => !mod.IsEnabled);
        DisableAllShownItem.IsEnabled = canEdit && FilteredMods.Any(mod => mod.IsEnabled);
        NoModsText.Text = _controller?.LoadError ?? (Mods.Count == 0 ? "No UE4SS mods installed. Use Install… or drop a ZIP or mod folder here." : "No mods match. Clear search or choose All mods.");
        NoModsText.Visibility = _controller?.LoadError is not null || FilteredMods.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshProfileList()
    {
        if (_controller is null)
            return;
        _isUpdatingProfiles = true;
        ProfileComboBox.ItemsSource = _controller.Profiles.Select(profile => profile.Name).ToList();
        ProfileComboBox.SelectedItem = _controller.ActiveProfile.Name;
        _isUpdatingProfiles = false;
    }

    private void ModTabContent_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not UIElement content || ModTypeTabs.SelectedItem is not TabViewItem tab || !ReferenceEquals(tab.Content, content))
            return;
        int index = ModTypeTabs.SelectedIndex;
        int previous = _previousModTabIndex;
        _previousModTabIndex = index;
        StopTabAnimation();
        if (previous < 0 || previous == index || !_uiSettings.AnimationsEnabled)
            return;
        ElementCompositionPreview.SetIsTranslationEnabled(content, true);
        var visual = ElementCompositionPreview.GetElementVisual(content);
        visual.Properties.InsertVector3(TranslationProperty, Vector3.Zero);
        visual.Opacity = 1;
        _animatedTabVisual = visual;
        using var easing = visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1), new Vector2(0.3f, 1));
        using var slide = visual.Compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0, new Vector3(index > previous ? 16 : -16, 0, 0));
        slide.InsertKeyFrame(1, Vector3.Zero, easing);
        slide.Duration = TimeSpan.FromMilliseconds(200);
        using var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0.65f);
        fade.InsertKeyFrame(1, 1, easing);
        fade.Duration = TimeSpan.FromMilliseconds(140);
        visual.StartAnimation(TranslationProperty, slide);
        visual.StartAnimation(nameof(Visual.Opacity), fade);
    }

    private void StopTabAnimation()
    {
        if (_animatedTabVisual is not { } visual)
            return;
        visual.StopAnimation(TranslationProperty);
        visual.StopAnimation(nameof(Visual.Opacity));
        visual.Properties.InsertVector3(TranslationProperty, Vector3.Zero);
        visual.Opacity = 1;
        _animatedTabVisual = null;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e) => await SaveModsAsync();
    private async void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_controller is not null)
            await _controller.LaunchAsync();
    }

    private async void FolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_controller is not null)
            await _controller.OpenGameFolderAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_controller is not null)
            await _controller.RefreshAsync();
    }

    private async void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUpdatingProfiles && ProfileComboBox.SelectedItem is string name && _controller is not null)
            await _controller.SelectProfileAsync(name);
    }

    private async void NewProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_controller is not null)
            await _controller.CreateProfileAsync();
    }

    private async void RenameProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_controller is not null)
            await _controller.RenameProfileAsync();
    }

    private async void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_controller is not null)
            await _controller.DeleteProfileAsync();
    }

    private async void ExportProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_controller is not null)
            await _controller.ExportProfileAsync();
    }

    private async void ImportProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_controller is not null)
            await _controller.ImportProfileAsync();
    }

    private async void InstallZipButton_Click(object sender, RoutedEventArgs e)
    {
        if (_controller is not null)
            await _controller.ImportZipAsync();
    }

    private async void ImportFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_controller is not null)
            await _controller.ImportFolderAsync();
    }

    private async void ModList_Drop(object sender, DragEventArgs e)
    {
        if (_controller is null || !e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            return;
        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            if (!_subscriptionsActive) return;
            await _controller.ImportDroppedAsync(items.Select(item => new DroppedMod(item.Path, item is Windows.Storage.StorageFolder)).ToList());
        }
        catch (Exception ex)
        {
            if (_subscriptionsActive) await _controller.ReportDropFailureAsync(ex);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private static void ModList_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
    }

    private static string? ModNameFrom(object sender) => (sender as MenuFlyoutItem)?.Tag as string;
    private async void OpenModFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ModNameFrom(sender)is { } name && _controller is not null)
            await _controller.OpenModFolderAsync(name);
    }

    private async void UninstallMod_Click(object sender, RoutedEventArgs e)
    {
        if (ModNameFrom(sender)is { } name && _controller is not null)
            await _controller.UninstallAsync(name);
    }

    private async void RepairMod_Click(object sender, RoutedEventArgs e)
    {
        if (ModNameFrom(sender)is { } name && _controller is not null)
            await _controller.RepairAsync(name);
    }

    private async void RestoreMod_Click(object sender, RoutedEventArgs e)
    {
        if (ModNameFrom(sender)is { } name && _controller is not null)
            await _controller.RestoreAsync(name);
    }

    private void ModActionsButton_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Flyout: MenuFlyout menu })
            return;
        menu.Opening -= ModActionsMenu_Opening;
        menu.Opening += ModActionsMenu_Opening;
    }

    private void ModActionsMenu_Opening(object? sender, object _)
    {
        if (sender is not MenuFlyout menu || _controller is null)
            return;
        foreach (object entry in menu.Items)
        {
            if (entry is not MenuFlyoutItem { Name: "EditConfigurationItem", Tag: string name } item)
                continue;
            item.IsEnabled = _controller.LoadError is null && _controller.HasModConfiguration(name);
            ToolTipService.SetToolTip(item, item.IsEnabled ? "Edit this mod's configuration files." : "No .ini, .cfg, .toml, or .json configuration files found.");
        }
    }

    private async void EditConfiguration_Click(object sender, RoutedEventArgs e)
    {
        if (ModNameFrom(sender)is { } name && _controller is not null)
            await _controller.EditConfigurationAsync(name);
    }

    private async void EnableAllShown_Click(object sender, RoutedEventArgs e)
    {
        if (_controller is not null)
            await _controller.ApplyAllAsync(FilteredMods.Select(mod => mod.Model), true);
    }

    private async void DisableAllShown_Click(object sender, RoutedEventArgs e)
    {
        if (_controller is not null)
            await _controller.ApplyAllAsync(FilteredMods.Select(mod => mod.Model), false);
    }

    private void ModSearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void ModFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();
}
