using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Neodash_Mod_Loader.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Neodash_Mod_Loader.Views;

public partial class ModLibraryItemViewModel : INotifyPropertyChanged
{
    public ModLibraryItem Mod { get; set; } = new();
    public string Name => Mod.Name;
    public string DeveloperName => Mod.DeveloperName;
    public string Subtitle => Mod.Subtitle;
    public string Version => "Available: v" + Mod.Version;
    public string InstalledText { get; set; } = string.Empty;
    public bool IsInstalled { get; set; }
    public bool HasUpdate { get; set; }
    public Visibility RepairVisibility { get; set; } = Visibility.Collapsed;
    public bool CanRepair { get; set; }
    public string ButtonText { get; set { field = value; OnPropertyChanged(); } } = "Install";
    public bool IsButtonEnabled { get; set { field = value; OnPropertyChanged(); } } = true;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed partial class ModLibraryPage : Page
{
    public ObservableCollection<ModLibraryItemViewModel> Mods { get; } = [];
    private ModLibraryResponse? _catalog;
    private bool _busy;
    private bool _loading;

    public ModLibraryPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => { ModOperations.Changed += OperationsChanged; await LoadLibraryAsync(); };
        Unloaded += (_, _) => ModOperations.Changed -= OperationsChanged;
    }

    private void OperationsChanged(object? sender, EventArgs args) => DispatcherQueue.TryEnqueue(ApplyFilter);

    private async Task LoadLibraryAsync()
    {
        if (_loading) return;
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
            _catalog = await ModOperations.LoadCatalogAsync();
            LibraryTitle.Text = _catalog.Name ?? "Mod Library";
            LibrarySubtitle.Text = _catalog.Subtitle ?? "Browse and install mods";
            ApplyFilter();
        }
        catch (Exception ex)
        {
            ErrorText.Text = "Couldn't refresh the library: " + ex.Message + " Use Refresh to try again.";
            ErrorText.Visibility = Visibility.Visible;
            if (_catalog is null) LibrarySubtitle.Text = "Library unavailable";
            AppStorage.Log("Library load failed.", ex);
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

    private static ModLibraryItemViewModel CreateModViewModel(ModLibraryItem mod)
    {
        var record = ModOperations.FindRecord(mod);
        var folders = ModOperations.GetInstalledFolders(mod);
        string? version = folders.Count > 0 ? record?.Version ?? SettingsManager.Current.GetInstalledModVersion(mod.Name) : null;
        bool installed = folders.Count > 0;
        bool incomplete = record is not null && folders.Count != record.FolderNames.Count;
        int? comparison = ModVersion.Compare(mod.Version, version);
        bool update = installed && comparison > 0;
        string status = "Not installed";
        string button = "Install";
        if (installed) { status = "Installed: " + (version ?? "version unknown"); button = "Installed"; }
        if (update) button = "Update";
        if (incomplete) { status = "Missing files — repair recommended"; button = "Repair"; }
        return new ModLibraryItemViewModel { Mod = mod, IsInstalled = installed, HasUpdate = update,
            InstalledText = status,
            ButtonText = button,
            RepairVisibility = installed && !incomplete && !update ? Visibility.Visible : Visibility.Collapsed,
            CanRepair = installed && GamePathResolver.GetNeodashWin64Path().Length > 0,
            IsButtonEnabled = GamePathResolver.GetNeodashWin64Path().Length > 0 && (!installed || incomplete || update)
        };
    }

    private void ApplyFilter()
    {
        if (_catalog is null) return;
        Mods.Clear();
        string search = SearchBox.Text.Trim();
        int filter = LibraryFilter.SelectedIndex;
        foreach (var mod in _catalog.Mods ?? [])
        {
            var item = CreateModViewModel(mod);
            if (search.Length > 0 && !mod.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                && !mod.DeveloperName.Contains(search, StringComparison.OrdinalIgnoreCase)) continue;
            if (filter == 1 && !item.IsInstalled || filter == 2 && !item.HasUpdate) continue;
            Mods.Add(item);
        }
        ModsListView.ItemsSource = Mods;
        EmptyLibraryText.Text = (_catalog.Mods?.Count ?? 0) == 0
            ? "The library has no mods yet. Use Refresh to check again."
            : "No mods match. Clear search or choose All mods.";
        EmptyLibraryText.Visibility = !_loading && Mods.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateAllButton.IsEnabled = !_busy && !_loading && Mods.Any(m => m.HasUpdate);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void LibraryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();
    private async void RefreshButton_Click(object sender, RoutedEventArgs e) { if (!_busy) await LoadLibraryAsync(); }

    private async Task InstallAsync(ModLibraryItem mod)
    {
        if (_busy || _loading || ModOperations.IsBusy) return;
        string? version = ModOperations.GetInstalledFolders(mod).Count > 0
            ? ModOperations.FindRecord(mod)?.Version ?? SettingsManager.Current.GetInstalledModVersion(mod.Name)
            : null;
        if (ModVersion.Compare(mod.Version, version) < 0 && !await UiOperations.ConfirmAsync(XamlRoot, "Install older version?",
            $"The library offers {mod.Version}, but {version} is already installed. Replace it with the older version?", "Install older version")) return;
        _busy = true;
        LibraryControls.IsEnabled = false;
        try { await UiOperations.RunAsync(XamlRoot, "Install " + mod.Name, (p, t) => ModOperations.InstallCatalogAsync(mod, p, t)); }
        finally { _busy = false; LibraryControls.IsEnabled = true; ApplyFilter(); }
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ModLibraryItemViewModel item }) await InstallAsync(item.Mod);
    }

    private async void OpenModFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _loading || ModOperations.IsBusy || sender is not FrameworkElement { Tag: ModLibraryItemViewModel item }) return;
        try
        {
            string game = GamePathResolver.RequireGamePath();
            var folders = ModOperations.GetInstalledFolders(item.Mod);
            if (folders.Count == 0) throw new DirectoryNotFoundException("The mod folder no longer exists. Refresh the library to update its status.");
            string? folder = folders.Count == 1 ? folders[0] : await ChooseModFolderAsync(item.Name, folders);
            if (folder is null) return;
            var installed = ModManager.GetMods(game).Find(mod => mod.Name.Equals(folder, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(installed.Name)) throw new DirectoryNotFoundException("The mod folder no longer exists. Refresh the library to update its status.");
            string path = SafePaths.Child(game, Path.GetRelativePath(game, ModManager.GetModPath(game, installed.Name, installed.IsEnabled)));
            UiFiles.OpenPath(path);
        }
        catch (Exception ex) { await UiOperations.ShowMessageAsync(XamlRoot, "Couldn't open mod folder", ex.Message); }
    }

    private async Task<string?> ChooseModFolderAsync(string name, List<string> folders)
    {
        var picker = new ComboBox { Header = "Mod folder", ItemsSource = folders, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var dialog = new ContentDialog { Title = "Open " + name + " folder", Content = picker, PrimaryButtonText = "Open folder", CloseButtonText = "Cancel", XamlRoot = XamlRoot };
        return await UiOperations.ShowDialogAsync(dialog) == ContentDialogResult.Primary ? picker.SelectedItem as string : null;
    }

    private async void UninstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _loading || ModOperations.IsBusy || sender is not FrameworkElement { Tag: ModLibraryItemViewModel item }) return;
        var folders = ModOperations.FindRecord(item.Mod)?.FolderNames ?? ModOperations.GetInstalledFolders(item.Mod);
        if (folders.Count == 0) return;
        if (!await UiOperations.ConfirmAsync(XamlRoot, "Delete mod?", $"Permanently delete {string.Join(", ", folders)} and remove it from profiles for this game installation?", "Uninstall")) return;
        _busy = true;
        LibraryControls.IsEnabled = false;
        try { await UiOperations.RunAsync(XamlRoot, "Uninstall " + item.Name, (_, _) => ModOperations.UninstallCatalogAsync(item.Mod), allowCancellation: false); }
        finally { _busy = false; LibraryControls.IsEnabled = true; ApplyFilter(); }
    }

    private async void UpdateAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _loading || ModOperations.IsBusy) return;
        var updates = (_catalog?.Mods ?? []).Where(m => CreateModViewModel(m).HasUpdate).ToList();
        if (updates.Count == 0) return;
        _busy = true;
        LibraryControls.IsEnabled = false;
        try
        {
            await UiOperations.RunAsync(XamlRoot, "Update mods", async (progress, token) =>
            {
                foreach (var mod in updates) { token.ThrowIfCancellationRequested(); await ModOperations.InstallCatalogAsync(mod, progress, token); }
            });
        }
        finally { _busy = false; LibraryControls.IsEnabled = true; ApplyFilter(); }
    }
}
