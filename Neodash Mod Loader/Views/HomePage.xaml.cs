using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Neodash_Mod_Loader.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using Windows.UI.ViewManagement;

namespace Neodash_Mod_Loader.Views;

public partial class ModItem : INotifyPropertyChanged
{
    public string Name { get; set; } = string.Empty;
    public Windows.UI.Text.FontWeight FontWeight { get; set; }
    public bool OriginalIsEnabled { get; set; }
    public bool CanRepair { get; set; }
    public bool CanRestore { get; set; }
    public string ManagedVersion { get; set; } = string.Empty;
    public Visibility ManagedVersionVisibility => string.IsNullOrWhiteSpace(ManagedVersion) ? Visibility.Collapsed : Visibility.Visible;

    public string EntryPointPath
    {
        get;
        set { if (field != value) { field = value; OnPropertyChanged(); } }
    } = string.Empty;

    public bool IsEnabled
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
                HomePage.Current?.CheckDirtyState();
                HomePage.Current?.QueueFilter();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed partial class HomePage : Page
{
    public static HomePage? Current { get; private set; }
    public ObservableCollection<ModItem> Mods { get; } = [];
    public ObservableCollection<ModItem> FilteredMods { get; } = [];
    public bool HasUnsavedChanges { get; private set; }
    private bool _isUpdatingProfiles;
    private string? _loadError;
    private readonly UISettings _uiSettings = new();
    private int _previousModTabIndex = -1;
    private Visual? _animatedTabVisual;

    public HomePage()
    {
        Current = this;
        InitializeComponent();
        InstalledModsList.DragOver += ModList_DragOver;
        ModOperations.Changed += ModOperations_Changed;
        Unloaded += HomePage_Unloaded;
        LoadMods();
        RefreshProfileList();
    }

    private void HomePage_Unloaded(object sender, RoutedEventArgs e)
    {
        StopTabAnimation();
        _previousModTabIndex = -1;
        ModOperations.Changed -= ModOperations_Changed;
        Unloaded -= HomePage_Unloaded;
        if (Current == this) Current = null;
    }

    private void ModTabContent_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not UIElement content || ModTypeTabs.SelectedItem is not TabViewItem tab || !ReferenceEquals(tab.Content, content)) return;
        int index = ModTypeTabs.SelectedIndex;
        int previous = _previousModTabIndex;
        _previousModTabIndex = index;
        StopTabAnimation();
        if (previous < 0 || previous == index || !_uiSettings.AnimationsEnabled) return;

        ElementCompositionPreview.SetIsTranslationEnabled(content, true);
        var visual = ElementCompositionPreview.GetElementVisual(content);
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
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
        visual.StartAnimation("Translation", slide);
        visual.StartAnimation(nameof(Visual.Opacity), fade);
    }

    private void StopTabAnimation()
    {
        if (_animatedTabVisual is not { } visual) return;
        visual.StopAnimation("Translation");
        visual.StopAnimation(nameof(Visual.Opacity));
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
        visual.Opacity = 1;
        _animatedTabVisual = null;
    }

    private void ModOperations_Changed(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(RefreshMods);

    public void CheckDirtyState()
    {
        HasUnsavedChanges = Mods.Any(mod => mod.IsEnabled != mod.OriginalIsEnabled);
        SaveButton.IsEnabled = HasUnsavedChanges && !ModOperations.IsBusy;
    }

    public void ApplyFilter()
    {
        if (NoModsText is null) return;
        string search = ModSearchBox?.Text.Trim() ?? string.Empty;
        int filter = ModFilterComboBox?.SelectedIndex ?? 0;
        IEnumerable<ModItem> items = Mods;
        if (!string.IsNullOrEmpty(search))
            items = items.Where(mod => mod.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || mod.EntryPointPath.Contains(search, StringComparison.OrdinalIgnoreCase));
        items = filter switch { 1 => items.Where(mod => mod.IsEnabled), 2 => items.Where(mod => !mod.IsEnabled), _ => items };
        FilteredMods.Clear();
        foreach (var mod in items) FilteredMods.Add(mod);
        NoModsText.Text = _loadError ?? (Mods.Count == 0
            ? "No UE4SS mods installed. Use Install… or drop a ZIP or mod folder here."
            : "No mods match. Clear search or choose All mods.");
        NoModsText.Visibility = _loadError is not null || FilteredMods.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void QueueFilter() => DispatcherQueue.TryEnqueue(ApplyFilter);

    private static string ReadEntryPointPath(string gamePath, string modPath)
    {
        try { return ModEntryPoints.GetDisplayPath(gamePath, modPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppStorage.Log("Could not read a mod's entry point.", ex);
            return string.Empty;
        }
    }

    private void LoadMods()
    {
        _loadError = null;
        try
        {
            string win64Path = GamePathResolver.RequireGamePath();
            if (!GameRuntime.IsGameRunning(win64Path)) MigrateModsTxtIfNeeded(win64Path);
            foreach (var mod in ModManager.GetMods(win64Path).OrderBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase))
            {
                var record = ModOperations.FindRecordForFolder(mod.Name);
                Mods.Add(new ModItem
                {
                    Name = mod.Name,
                    FontWeight = Microsoft.UI.Text.FontWeights.Normal,
                    EntryPointPath = ReadEntryPointPath(win64Path, ModManager.GetModPath(win64Path, mod.Name, mod.IsEnabled)),
                    IsEnabled = mod.IsEnabled,
                    OriginalIsEnabled = mod.IsEnabled,
                    CanRepair = record is { DownloadUrl: { Length: > 0 } },
                    CanRestore = record?.BackupPath is string backup && Directory.Exists(backup),
                    ManagedVersion = record is null ? string.Empty : $"Tracked: {record.Version}"
                });
            }
            CheckDirtyState();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            AppStorage.Log("Unable to load installed mods.", ex);
            _loadError = "Could not load installed mods: " + ex.Message + " Check the game folder in Settings, then Refresh.";
            Mods.Clear();
            CheckDirtyState();
            ApplyFilter();
        }
    }

    private void RefreshMods()
    {
        Mods.Clear();
        FilteredMods.Clear();
        LoadMods();
        RefreshProfileList();
    }

    private static void MigrateModsTxtIfNeeded(string win64Path)
    {
        _ = SettingsMigration.MigrateLegacyModState(win64Path);
    }

    private async Task<bool> EnsureGameClosedAsync()
    {
        try { GameRuntime.EnsureGameClosed(GamePathResolver.RequireGamePath()); return true; }
        catch (Exception ex) { await ShowMessageAsync("Close Neodash first", ex.Message); return false; }
    }

    private async Task<bool> EnsureNotBusyAsync()
    {
        if (!ModOperations.IsBusy) return true;
        await ShowMessageAsync("Another mod operation is running", "Wait for the current install, update, repair, or uninstall to finish before making more changes.");
        return false;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e) => await SaveModsAsync();

    private async void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureNotBusyAsync() || !await ResolveUnsavedChangesAsync()) return;
        try
        {
            GamePathResolver.RequireGamePath();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = Properties.ResourceStrings.GetRequired("NeodashLaunchUri"), UseShellExecute = true });
        }
        catch (Exception ex) { await ShowMessageAsync("Couldn't launch Neodash", ex.Message); }
    }

    private void FolderButton_Click(object sender, RoutedEventArgs e)
    {
        try { UiFiles.OpenPath(GamePathResolver.RequireGamePath()); }
        catch (Exception ex) { _ = ShowMessageAsync("Couldn't open the game folder", ex.Message); }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (await ResolveUnsavedChangesAsync()) RefreshMods();
    }

    public void DiscardChanges()
    {
        foreach (var mod in Mods) mod.IsEnabled = mod.OriginalIsEnabled;
        CheckDirtyState();
    }

    public async Task<bool> SaveModsAsync()
    {
        if (!HasUnsavedChanges) return true;
        if (!await EnsureNotBusyAsync()) return false;
        if (!await EnsureGameClosedAsync()) return false;
        string win64Path;
        try
        {
            ProfileManager.EnsureWritable();
            win64Path = GamePathResolver.RequireGamePath();
            ProfileManager.EnsureProfileGamePath(ProfileManager.ActiveProfile, win64Path);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Can't save mod changes", ex.Message);
            return false;
        }
        var changed = Mods.Where(mod => mod.IsEnabled != mod.OriginalIsEnabled).ToList();
        try
        {
            ModStateTransactions.ApplyChanges(win64Path, changed.ToDictionary(mod => mod.Name, mod => mod.IsEnabled, StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            AppStorage.Log("Could not save mod states and profile.", ex);
            CheckDirtyState();
            await ShowMessageAsync("Couldn't save mod changes", ex.Message);
            return false;
        }
        foreach (var mod in changed)
        {
            mod.OriginalIsEnabled = mod.IsEnabled;
            try { mod.EntryPointPath = ReadEntryPointPath(win64Path, ModManager.GetModPath(win64Path, mod.Name, mod.IsEnabled)); }
            catch (Exception ex) { mod.EntryPointPath = string.Empty; AppStorage.Log("Could not read a mod's entry point.", ex); }
        }
        CheckDirtyState();
        ApplyFilter();
        return true;
    }

    private async Task<bool> ResolveUnsavedChangesAsync()
    {
        if (!HasUnsavedChanges) return true;
        var dialog = new ContentDialog { Title = "Unsaved Changes", Content = $"Save changes to \"{ProfileManager.ActiveProfile.Name}\" before continuing?", PrimaryButtonText = "Save", SecondaryButtonText = "Discard", CloseButtonText = "Cancel", XamlRoot = XamlRoot };
        return (await UiOperations.ShowDialogAsync(dialog)) switch
        {
            ContentDialogResult.Primary => await SaveModsAsync(),
            ContentDialogResult.Secondary => DiscardAndContinue(),
            _ => false
        };
    }

    private bool DiscardAndContinue() { DiscardChanges(); return true; }

    private async Task<bool> SwitchToProfileAsync(ModProfile target)
    {
        if (!await EnsureNotBusyAsync()) return false;
        if (!await EnsureGameClosedAsync()) return false;
        string win64Path;
        try
        {
            ProfileManager.EnsureWritable();
            win64Path = GamePathResolver.RequireGamePath();
            ProfileManager.EnsureProfileGamePath(target, win64Path);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Can't switch profiles", ex.Message);
            return false;
        }
        var installedMods = ModManager.GetMods(win64Path);
        var installed = new HashSet<string>(installedMods.Select(mod => mod.Name), StringComparer.OrdinalIgnoreCase);
        var missing = target.EnabledMods.Where(name => !installed.Contains(name)).ToList();
        var previousEnabled = installedMods.Where(mod => mod.IsEnabled).Select(mod => mod.Name).ToList();
        if (missing.Count > 0) await ShowErrorsAsync("Profile has missing mods", "Those mods are not installed and will stay disabled.", missing);
        try { GameRuntime.EnsureGameClosed(win64Path); }
        catch (Exception ex)
        {
            await ShowMessageAsync("Close Neodash first", ex.Message);
            return false;
        }
        var errors = ModManager.ApplyEnabledMods(win64Path, target.EnabledMods);
        if (errors.Count > 0)
        {
            await ShowErrorsAsync("Couldn't switch profiles", $"Your mods were left as they were in \"{ProfileManager.ActiveProfile.Name}\".", errors);
            return false;
        }
        try { ProfileManager.SetActiveProfile(target, win64Path); }
        catch (Exception ex)
        {
            var rollbackErrors = ModManager.ApplyEnabledMods(win64Path, previousEnabled);
            string detail = rollbackErrors.Count == 0
                ? "The original mod set was restored."
                : "The profile was not saved, and some folders could not be restored:\n" + string.Join("\n", rollbackErrors);
            await ShowMessageAsync("Profile switch needs attention", "The selected profile could not be saved: " + ex.Message + "\n\n" + detail);
            return false;
        }
        RefreshMods();
        return true;
    }

    private void RefreshProfileList()
    {
        _isUpdatingProfiles = true;
        ProfileComboBox.ItemsSource = ProfileManager.Profiles.Select(profile => profile.Name).ToList();
        ProfileComboBox.SelectedItem = ProfileManager.ActiveProfile.Name;
        DeleteProfileButton.IsEnabled = ProfileManager.Profiles.Count > 1;
        _isUpdatingProfiles = false;
    }

    private async void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingProfiles || ProfileComboBox.SelectedItem is not string name || name == ProfileManager.ActiveProfile.Name) return;
        var target = ProfileManager.Profiles.First(profile => profile.Name == name);
        if (!await ResolveUnsavedChangesAsync() || !await SwitchToProfileAsync(target)) RefreshProfileList();
    }

    private async void NewProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await ResolveUnsavedChangesAsync()) return;
        string? name = await PromptForProfileNameAsync("New Profile", "Create", "", null, "The new profile starts with the mods enabled right now.");
        if (name == null) return;
        try { ProfileManager.CreateProfile(name, GamePathResolver.RequireGamePath()); RefreshMods(); }
        catch (Exception ex) { await ShowMessageAsync("Couldn't create profile", ex.Message); }
    }

    private async void RenameProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await ResolveUnsavedChangesAsync()) return;
        var profile = ProfileManager.ActiveProfile;
        string? name = await PromptForProfileNameAsync("Rename Profile", "Rename", profile.Name, profile, null);
        if (name == null) return;
        try { ProfileManager.RenameProfile(profile, name); RefreshProfileList(); }
        catch (Exception ex) { await ShowMessageAsync("Couldn't rename profile", ex.Message); }
    }

    private async void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await ResolveUnsavedChangesAsync()) return;
        var profile = ProfileManager.ActiveProfile;
        var fallback = ProfileManager.Profiles.FirstOrDefault(item => item != profile);
        if (fallback == null || !await UiOperations.ConfirmAsync(XamlRoot, "Delete Profile", $"Delete \"{profile.Name}\" and switch to \"{fallback.Name}\"? Installed mods are kept.", "Delete")) return;
        if (!await SwitchToProfileAsync(fallback)) return;
        try { ProfileManager.DeleteProfile(profile); RefreshProfileList(); }
        catch (Exception ex) { await ShowMessageAsync("Couldn't delete profile", ex.Message); }
    }

    private async void ExportProfileButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!await ResolveUnsavedChangesAsync()) return;
            string? path = await UiFiles.SaveFileAsync($"{ProfileManager.ActiveProfile.Name}.neodash-profile", ".json");
            if (path != null) ProfileManager.ExportProfile(path);
        }
        catch (Exception ex) { await ShowMessageAsync("Couldn't export profile", ex.Message); }
    }

    private async void ImportProfileButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!await ResolveUnsavedChangesAsync()) return;
            string? path = await UiFiles.PickFileAsync(".json");
            if (path == null) return;
            ModProfile profile = ProfileManager.ImportProfile(path);
            RefreshProfileList();
            if (await UiOperations.ConfirmAsync(XamlRoot, "Profile imported", $"Switch to imported profile \"{profile.Name}\" now?", "Switch")) await SwitchToProfileAsync(profile);
        }
        catch (Exception ex) { await ShowMessageAsync("Couldn't import profile", ex.Message); }
    }

    private async void InstallZipButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string? path = await UiFiles.PickFileAsync(".zip");
            if (path != null && await ResolveUnsavedChangesAsync()) await RunManagedOperationAsync("Installing mod", (progress, token) => ModOperations.ImportZipAsync(path, progress, token));
        }
        catch (Exception ex) { await ShowMessageAsync("Couldn't install mod", ex.Message); }
    }

    private async void ImportFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string? path = await UiFiles.PickFolderAsync();
            if (path != null && await ResolveUnsavedChangesAsync()) await RunManagedOperationAsync("Importing mod folder", (progress, token) => ModOperations.ImportFolderAsync(path, progress, token));
        }
        catch (Exception ex) { await ShowMessageAsync("Couldn't import mod folder", ex.Message); }
    }

    private async void ModList_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)) return;
        try
        {
            if (!await EnsureNotBusyAsync()) return;
            IReadOnlyList<Windows.Storage.IStorageItem> items;
            var deferral = e.GetDeferral();
            try { items = await e.DataView.GetStorageItemsAsync(); }
            finally { deferral.Complete(); }
            if (items.Count == 0) return;
            foreach (var item in items)
                if (item is not Windows.Storage.StorageFolder && !Path.GetExtension(item.Path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Drop mod ZIP files or mod folders. Other formats are not supported.");
            if (!await ResolveUnsavedChangesAsync()) return;
            await RunManagedOperationAsync("Installing dropped mods", async (progress, token) =>
            {
                foreach (var item in items)
                {
                    token.ThrowIfCancellationRequested();
                    if (item is Windows.Storage.StorageFolder) await ModOperations.ImportFolderAsync(item.Path, progress, token);
                    else await ModOperations.ImportZipAsync(item.Path, progress, token);
                }
            });
        }
        catch (Exception ex) { await ShowMessageAsync("Couldn't import dropped mods", ex.Message); }
    }

    private static void ModList_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
    }

    private async Task RunManagedOperationAsync(string title, Func<IProgress<OperationProgress>, CancellationToken, Task> action, bool allowCancellation = true)
    {
        if (await UiOperations.RunAsync(XamlRoot, title, action, allowCancellation)) RefreshMods();
    }

    private static string? ModNameFrom(object sender) => (sender as MenuFlyoutItem)?.Tag as string;

    private async void OpenModFolder_Click(object sender, RoutedEventArgs e)
    {
        string? name = ModNameFrom(sender);
        if (name == null) return;
        try
        {
            string game = GamePathResolver.RequireGamePath();
            var mod = ModManager.GetMods(game).FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(mod.Name)) throw new DirectoryNotFoundException("The mod folder no longer exists.");
            UiFiles.OpenPath(ModManager.GetModPath(game, mod.Name, mod.IsEnabled));
        }
        catch (Exception ex) { await ShowMessageAsync("Couldn't open mod folder", ex.Message); }
    }

    private async void UninstallMod_Click(object sender, RoutedEventArgs e)
    {
        string? name = ModNameFrom(sender);
        if (name == null || !await ResolveUnsavedChangesAsync()) return;
        var record = ModOperations.FindRecordForFolder(name);
        var folders = record?.FolderNames?.Count > 0 ? record.FolderNames : [name];
        string detail = folders.Count > 1 ? $" This package owns {folders.Count} folders, and all will be deleted." : string.Empty;
        if (!await UiOperations.ConfirmAsync(XamlRoot, "Uninstall mod", $"Delete {string.Join(", ", folders)} from Neodash? This cannot be undone.{detail}", "Uninstall")) return;
        await RunManagedOperationAsync("Uninstalling mod", async (_, _) =>
        {
            await ModOperations.UninstallFolderAsync(name);
        }, allowCancellation: false);
    }

    private async void RepairMod_Click(object sender, RoutedEventArgs e)
    {
        string? name = ModNameFrom(sender);
        if (name == null || !await ResolveUnsavedChangesAsync()) return;
        if (ModOperations.FindRecordForFolder(name) is null) { await ShowMessageAsync("Can't repair this mod", "Only mods installed by the loader can be repaired. Import a ZIP to manage this mod."); return; }
        await RunManagedOperationAsync("Repairing mod", (progress, token) => ModOperations.ReinstallFolderAsync(name, progress, token));
    }

    private async void RestoreMod_Click(object sender, RoutedEventArgs e)
    {
        string? name = ModNameFrom(sender);
        if (name == null || !await ResolveUnsavedChangesAsync()) return;
        var record = ModOperations.FindRecordForFolder(name);
        if (record?.BackupPath is not string backup || !Directory.Exists(backup)) { await ShowMessageAsync("No backup available", "There is no update backup for this managed mod."); return; }
        await RunManagedOperationAsync("Restoring backup", (_, _) => ModOperations.RestoreFolderAsync(name), allowCancellation: false);
    }

    private async void EditConfiguration_Click(object sender, RoutedEventArgs e)
    {
        string? name = ModNameFrom(sender);
        if (name == null || !await EnsureNotBusyAsync() || !await ResolveUnsavedChangesAsync() || !await EnsureGameClosedAsync()) return;
        try
        {
            string game = GamePathResolver.RequireGamePath();
            var mod = ModManager.GetMods(game).FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(mod.Name)) throw new DirectoryNotFoundException("The mod folder no longer exists.");
            string root = SafePaths.Child(game, Path.GetRelativePath(game, ModManager.GetModPath(game, mod.Name, mod.IsEnabled)));
            string[] files = UiConfigurationEditor.FindFiles(game, root);
            if (files.Length == 0) { await ShowMessageAsync("No configuration found", "This mod has no .ini, .cfg, .toml, or .json files."); return; }
            await EditConfigFileAsync(game, root, files);
        }
        catch (Exception ex) { await ShowMessageAsync("Couldn't edit configuration", ex.Message); }
    }

    private Task EditConfigFileAsync(string gameRoot, string root, IReadOnlyList<string> files) =>
        UiConfigurationEditor.EditAsync(XamlRoot, gameRoot, root, files);

    private async void EnableAllShown_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureNotBusyAsync()) return;
        foreach (var mod in FilteredMods.ToList()) mod.IsEnabled = true;
        await SaveModsAsync();
    }

    private async void DisableAllShown_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureNotBusyAsync()) return;
        foreach (var mod in FilteredMods.ToList()) mod.IsEnabled = false;
        await SaveModsAsync();
    }
    private void ModSearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void ModFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();

    private async Task ShowMessageAsync(string title, string message) => await UiOperations.ShowMessageAsync(XamlRoot, title, message);
    private async Task ShowErrorsAsync(string title, string message, IEnumerable<string> errors) => await ShowMessageAsync(title, message + "\n\n" + string.Join("\n", errors.Select(error => "• " + error)));

    private async Task<string?> PromptForProfileNameAsync(string title, string primaryButtonText, string initialName, ModProfile? renaming, string? description)
    {
        var nameBox = new TextBox { Text = initialName, PlaceholderText = "Profile name", MaxLength = ProfileManager.MaxNameLength };
        var content = new StackPanel { Spacing = 12 };
        if (description != null) content.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(nameBox);
        var dialog = new ContentDialog { Title = title, Content = content, PrimaryButtonText = primaryButtonText, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary, XamlRoot = XamlRoot };
        void Validate() { string? error = ProfileManager.ValidateName(nameBox.Text, renaming); dialog.IsPrimaryButtonEnabled = error == null; nameBox.Description = string.IsNullOrWhiteSpace(nameBox.Text) ? null : error; }
        nameBox.TextChanged += (_, _) => Validate();
        Validate();
        return await UiOperations.ShowDialogAsync(dialog) == ContentDialogResult.Primary ? nameBox.Text.Trim() : null;
    }
}
