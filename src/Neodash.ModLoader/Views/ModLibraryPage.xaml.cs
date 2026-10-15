using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Neodash_Mod_Loader.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Neodash_Mod_Loader.Services;

namespace Neodash_Mod_Loader.Views;

public sealed partial class ModLibraryPage : Page
{
    public ObservableCollection<ModLibraryItemViewModel> Mods { get; } = [];

    private ModLibraryResponse? _catalog;
    private bool _busy;
    private bool _loading;
    private bool _active;
    private ModLibraryController? _controller;
    private ModLibraryController Controller => _controller ?? throw new InvalidOperationException("The mod library has not been activated.");

    public ModLibraryPage()
    {
        InitializeComponent();
        Loaded += (_, _) => StartPage();
        Unloaded += (_, _) =>
        {
            if (!_active)
                return;
            Controller.Changed -= OperationsChanged;
            _active = false;
        };
    }

    internal void Activate(ModLibraryController controller)
    {
        _controller = controller;
        if (IsLoaded)
            StartPage();
    }

    private void StartPage()
    {
        if (_controller is null || _active)
            return;
        _active = true;
        Controller.Changed += OperationsChanged;
        _ = LoadLibraryAsync();
    }

    private void OperationsChanged(object? sender, EventArgs args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_active)
            ApplyFilter();
    });
    private async Task LoadLibraryAsync()
    {
        if (_loading)
            return;
        _loading = true;
        RefreshButton.IsEnabled = false;
        ModsListView.IsEnabled = false;
        UpdateAllButton.IsEnabled = false;
        EmptyLibraryText.Visibility = Visibility.Collapsed;
        LoadingRing.Visibility = Visibility.Visible;
        LoadingRing.IsActive = true;
        ErrorText.Visibility = Visibility.Collapsed;
        try
        {
            _catalog = await Controller.LoadAsync();
            LibraryTitle.Text = _catalog.Name ?? "Mod Library";
            LibrarySubtitle.Text = _catalog.Subtitle ?? "Browse and install mods";
            ApplyFilter();
        }
        catch (Exception ex)
        {
            ErrorText.Text = "Couldn't refresh the library: " + ex.Message + " Use Refresh to try again.";
            ErrorText.Visibility = Visibility.Visible;
            if (_catalog is null)
                LibrarySubtitle.Text = "Library unavailable";
            Controller.LogLoadFailure(ex);
        }
        finally
        {
            _loading = false;
            RefreshButton.IsEnabled = true;
            ModsListView.IsEnabled = true;
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
            ApplyFilter();
        }
    }

    private ModLibraryItemViewModel CreateModViewModel(ModLibraryItem mod)
    {
        var state = Controller.GetState(mod);
        return new ModLibraryItemViewModel
        {
            Mod = mod,
            IsInstalled = state.IsInstalled,
            HasUpdate = state.HasUpdate,
            InstalledText = state.Status,
            ButtonText = state.ButtonText,
            RepairVisibility = state.ShowRepair ? Visibility.Visible : Visibility.Collapsed,
            ActionsVisibility = state.HasMenuActions ? Visibility.Visible : Visibility.Collapsed,
            CanRepair = state.CanRepair,
            IsButtonEnabled = state.CanInstall
        };
    }

    private void ApplyFilter()
    {
        if (_catalog is null)
            return;
        Mods.Clear();
        string search = SearchBox.Text.Trim();
        int filter = LibraryFilter.SelectedIndex;
        foreach (var mod in _catalog.Mods ?? [])
        {
            var item = CreateModViewModel(mod);
            if (search.Length > 0 && !mod.Name.Contains(search, StringComparison.OrdinalIgnoreCase) && !mod.DeveloperName.Contains(search, StringComparison.OrdinalIgnoreCase))
                continue;
            if (filter == 1 && !item.IsInstalled || filter == 2 && !item.HasUpdate)
                continue;
            Mods.Add(item);
        }

        ModsListView.ItemsSource = Mods;
        EmptyLibraryText.Text = (_catalog.Mods?.Count ?? 0) == 0 ? "The library has no mods yet. Use Refresh to check again." : "No mods match. Clear search or choose All mods.";
        EmptyLibraryText.Visibility = !_loading && Mods.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateAllButton.IsEnabled = !_busy && !_loading && !Controller.IsBusy
            && (_catalog.Mods ?? []).Any(mod => Controller.GetState(mod).HasUpdate);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void LibraryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();
    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_busy)
            await LoadLibraryAsync();
    }

    private async Task InstallAsync(ModLibraryItem mod)
    {
        if (_busy || _loading || Controller.IsBusy)
            return;
        _busy = true;
        LibraryControls.IsEnabled = false;
        try
        {
            await Controller.InstallAsync(mod);
        }
        finally
        {
            _busy = false;
            LibraryControls.IsEnabled = true;
            ApplyFilter();
        }
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ModLibraryItemViewModel item })
            await InstallAsync(item.Mod);
    }

    private async void OpenModFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _loading || Controller.IsBusy || sender is not FrameworkElement { Tag: ModLibraryItemViewModel item })
            return;
        await Controller.OpenFolderAsync(item.Mod);
    }

    private async void UninstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _loading || Controller.IsBusy || sender is not FrameworkElement { Tag: ModLibraryItemViewModel item })
            return;
        _busy = true;
        LibraryControls.IsEnabled = false;
        try
        {
            await Controller.UninstallAsync(item.Mod);
        }
        finally
        {
            _busy = false;
            LibraryControls.IsEnabled = true;
            ApplyFilter();
        }
    }

    private async void UpdateAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _loading || Controller.IsBusy)
            return;
        _busy = true;
        LibraryControls.IsEnabled = false;
        try
        {
            await Controller.UpdateAllAsync(_catalog?.Mods ?? []);
        }
        finally
        {
            _busy = false;
            LibraryControls.IsEnabled = true;
            ApplyFilter();
        }
    }
}
