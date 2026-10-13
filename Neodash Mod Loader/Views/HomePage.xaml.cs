using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Neodash_Mod_Loader.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Neodash_Mod_Loader.Views
{
    public class ModItem : INotifyPropertyChanged
    {
        private bool _isEnabled;
        private string _scriptPath;

        public string Name { get; set; }
        public Windows.UI.Text.FontWeight FontWeight { get; set; }
        public bool OriginalIsEnabled { get; set; }

        public string ScriptPath
        {
            get => _scriptPath;
            set
            {
                if (_scriptPath != value)
                {
                    _scriptPath = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged();
                    HomePage.Current?.CheckDirtyState();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public sealed partial class HomePage : Page
    {
        private const string ModsTxtMigratedKey = "ModsTxtMigrated";

        public static HomePage Current { get; private set; }
        public ObservableCollection<ModItem> Mods { get; } = new ObservableCollection<ModItem>();

        public bool HasUnsavedChanges { get; private set; }

        // Set while the profile list is rebuilt, so ProfileComboBox_SelectionChanged ignores the resulting events.
        private bool _isUpdatingProfiles;

        public HomePage()
        {
            Current = this;
            this.InitializeComponent();
            LoadMods();
            RefreshProfileList();
        }

        public void CheckDirtyState()
        {
            HasUnsavedChanges = Mods.Any(m => m.IsEnabled != m.OriginalIsEnabled);
            SaveButton.IsEnabled = HasUnsavedChanges;
        }

        private string DetermineScriptPath(string modDirPath)
        {
            string scriptsPath = Path.Combine(modDirPath, "Scripts");
            if (!Directory.Exists(scriptsPath))
            {
                scriptsPath = Path.Combine(modDirPath, "Script");
            }

            if (Directory.Exists(scriptsPath))
            {
                var files = Directory.GetFiles(scriptsPath, "*.lua");
                if (files.Length > 0)
                {
                    string parentDirName = Path.GetFileName(Path.GetDirectoryName(modDirPath));
                    string modDirName = Path.GetFileName(modDirPath);
                    string fileName = Path.GetFileName(files[0]);
                    return $"\\{parentDirName}\\{modDirName}\\{Path.GetFileName(scriptsPath)}\\{fileName}";
                }
            }
            return "";
        }

        private void LoadMods()
        {
            NoModsText.Text = "No mods found.";
            NoModsText.Visibility = Visibility.Collapsed;

            string win64Path = GamePathResolver.GetNeodashWin64Path();

            try
            {
                MigrateModsTxtIfNeeded(win64Path);
                ProfileManager.SyncActiveProfile(win64Path);

                // A mod is enabled if its folder is in Mods and disabled if it's in DisabledMods.
                var mods = ModManager.GetMods(win64Path);
                if (mods.Count == 0)
                {
                    NoModsText.Visibility = Visibility.Visible;
                    return;
                }

                foreach (var mod in mods.OrderBy(m => m.Name))
                {
                    Mods.Add(new ModItem
                    {
                        Name = mod.Name,
                        FontWeight = Microsoft.UI.Text.FontWeights.Normal,
                        ScriptPath = DetermineScriptPath(ModManager.GetModPath(win64Path, mod.Name, mod.IsEnabled)),
                        IsEnabled = mod.IsEnabled,
                        OriginalIsEnabled = mod.IsEnabled
                    });
                }

                CheckDirtyState();
            }
            catch (Exception)
            {
                NoModsText.Text = "Error loading mods.";
                NoModsText.Visibility = Visibility.Visible;
            }
        }

        // Older versions disabled mods with "Name : 0" in mods.txt. Move those into DisabledMods once so they stay disabled.
        private static void MigrateModsTxtIfNeeded(string win64Path)
        {
            if (SettingsManager.Values.TryGetValue(ModsTxtMigratedKey, out object migratedObj) && migratedObj is bool migrated && migrated)
            {
                return;
            }

            // If a mod couldn't be moved (e.g. the game is running), leave the flag unset so the next load retries.
            if (ModManager.MigrateModsTxtDisabledMods(win64Path))
            {
                SettingsManager.Values[ModsTxtMigratedKey] = true;
                SettingsManager.SaveSettings();
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            await SaveModsAsync();
        }

        private void LaunchButton_Click(object sender, RoutedEventArgs e)
        {
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "steam://rungameid/1514950",
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(processInfo);
        }

        private void FolderButton_Click(object sender, RoutedEventArgs e)
        {
            string win64Path = GamePathResolver.GetNeodashWin64Path();
            if (System.IO.Directory.Exists(win64Path))
            {
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = win64Path,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(processInfo);
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            Mods.Clear();
            LoadMods();
        }

        public void DiscardChanges()
        {
            foreach (var mod in Mods)
            {
                mod.IsEnabled = mod.OriginalIsEnabled;
            }
        }

        /// <summary>
        /// Applies pending toggles by moving mod folders between Mods and DisabledMods.
        /// Mods that fail to move stay pending and are listed in an error dialog. Returns true if every change applied.
        /// </summary>
        public async Task<bool> SaveModsAsync()
        {
            string win64Path = GamePathResolver.GetNeodashWin64Path();
            var errors = new List<string>();

            foreach (var mod in Mods.Where(m => m.IsEnabled != m.OriginalIsEnabled).ToList())
            {
                try
                {
                    ModManager.SetModEnabled(win64Path, mod.Name, mod.IsEnabled);
                    mod.OriginalIsEnabled = mod.IsEnabled;
                    mod.ScriptPath = DetermineScriptPath(ModManager.GetModPath(win64Path, mod.Name, mod.IsEnabled));
                }
                catch (Exception ex)
                {
                    errors.Add($"{mod.Name}: {ex.Message}");
                }
            }

            CheckDirtyState();
            ProfileManager.SyncActiveProfile(win64Path);

            if (errors.Count == 0)
            {
                return true;
            }

            await ShowErrorsAsync("Some mod changes weren't applied", "Make sure Neodash is closed, then try saving again.", errors);
            return false;
        }

        private async Task ShowErrorsAsync(string title, string message, IEnumerable<string> errors)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message + "\n\n" + string.Join("\n", errors.Select(error => "• " + error)),
                CloseButtonText = "OK",
                XamlRoot = App.MainWindow.Content.XamlRoot
            };
            await dialog.ShowAsync();
        }

        private void RefreshProfileList()
        {
            _isUpdatingProfiles = true;
            ProfileComboBox.ItemsSource = ProfileManager.Profiles.Select(p => p.Name).ToList();
            ProfileComboBox.SelectedItem = ProfileManager.ActiveProfile.Name;
            DeleteProfileButton.IsEnabled = ProfileManager.Profiles.Count > 1;
            _isUpdatingProfiles = false;
        }

        private async void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingProfiles || ProfileComboBox.SelectedItem is not string name || name == ProfileManager.ActiveProfile.Name)
            {
                return;
            }

            var target = ProfileManager.Profiles.First(p => p.Name == name);
            if (!await ResolveUnsavedChangesAsync() || !await SwitchToProfileAsync(target))
            {
                // Cancelled or failed: put the selection back on the profile that's still active.
                RefreshProfileList();
            }
        }

        // Asks what to do with pending toggles before switching profiles. Returns false if the user cancels or the save fails.
        private async Task<bool> ResolveUnsavedChangesAsync()
        {
            if (!HasUnsavedChanges)
            {
                return true;
            }

            var dialog = new ContentDialog
            {
                Title = "Unsaved Changes",
                Content = $"Would you like to save your mod changes to \"{ProfileManager.ActiveProfile.Name}\" before switching?",
                PrimaryButtonText = "Save",
                SecondaryButtonText = "Discard",
                CloseButtonText = "Cancel",
                XamlRoot = App.MainWindow.Content.XamlRoot
            };

            switch (await dialog.ShowAsync())
            {
                case ContentDialogResult.Primary:
                    return await SaveModsAsync();
                case ContentDialogResult.Secondary:
                    DiscardChanges();
                    return true;
                default:
                    return false;
            }
        }

        private async Task<bool> SwitchToProfileAsync(ModProfile target)
        {
            string win64Path = GamePathResolver.GetNeodashWin64Path();
            var errors = ModManager.ApplyEnabledMods(win64Path, target.EnabledMods);
            if (errors.Count > 0)
            {
                await ShowErrorsAsync("Couldn't switch profiles", $"Your mods were left as they were in \"{ProfileManager.ActiveProfile.Name}\". Make sure Neodash is closed, then try again.", errors);
                return false;
            }

            ProfileManager.SetActiveProfile(target);
            Mods.Clear();
            LoadMods();
            RefreshProfileList();
            return true;
        }

        private async void NewProfileButton_Click(object sender, RoutedEventArgs e)
        {
            string name = await PromptForProfileNameAsync("New Profile", "Create", "", null,
                "The new profile starts with the mods you have enabled right now.");
            if (name != null)
            {
                // Same enabled mods as the current profile, so nothing moves. Any unsaved toggles will save into the new profile.
                ProfileManager.CreateProfile(name);
                RefreshProfileList();
            }
        }

        private async void RenameProfileButton_Click(object sender, RoutedEventArgs e)
        {
            var profile = ProfileManager.ActiveProfile;
            string name = await PromptForProfileNameAsync("Rename Profile", "Rename", profile.Name, profile, null);
            if (name != null)
            {
                ProfileManager.RenameProfile(profile, name);
                RefreshProfileList();
            }
        }

        private async void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
        {
            var profile = ProfileManager.ActiveProfile;
            var fallback = ProfileManager.Profiles.FirstOrDefault(p => p != profile);
            if (fallback == null)
            {
                return;
            }

            string message = $"\"{profile.Name}\" will be deleted and your mods will switch to \"{fallback.Name}\". Installed mods aren't removed.";
            if (HasUnsavedChanges)
            {
                message += "\n\nYour unsaved mod changes will be lost.";
            }

            var dialog = new ContentDialog
            {
                Title = "Delete Profile",
                Content = message,
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = App.MainWindow.Content.XamlRoot
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            DiscardChanges();
            if (await SwitchToProfileAsync(fallback))
            {
                ProfileManager.DeleteProfile(profile);
                RefreshProfileList();
            }
        }

        // Shows a name prompt and returns the trimmed name, or null if cancelled. Pass the profile being renamed, if any.
        private async Task<string> PromptForProfileNameAsync(string title, string primaryButtonText, string initialName, ModProfile renaming, string description)
        {
            var nameBox = new TextBox
            {
                Text = initialName,
                PlaceholderText = "Profile name",
                MaxLength = ProfileManager.MaxNameLength
            };

            var content = new StackPanel { Spacing = 12 };
            if (description != null)
            {
                content.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap });
            }
            content.Children.Add(nameBox);

            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                PrimaryButtonText = primaryButtonText,
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = App.MainWindow.Content.XamlRoot
            };

            void Validate()
            {
                string error = ProfileManager.ValidateName(nameBox.Text, renaming);
                dialog.IsPrimaryButtonEnabled = error == null;
                // Don't nag about an empty box before the user has typed anything.
                nameBox.Description = string.IsNullOrWhiteSpace(nameBox.Text) ? null : error;
            }
            nameBox.TextChanged += (s, args) => Validate();
            Validate();

            return await dialog.ShowAsync() == ContentDialogResult.Primary ? nameBox.Text.Trim() : null;
        }
    }
}
