using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Neodash_Mod_Loader.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;

namespace Neodash_Mod_Loader.Views
{
    public class ModLibraryItemViewModel : INotifyPropertyChanged
    {
        private string _buttonText;
        private bool _isButtonEnabled;

        public ModLibraryItem Mod { get; set; }
        public string Name => Mod.Name;
        public string DeveloperName => Mod.DeveloperName;
        public string Subtitle => Mod.Subtitle;
        public string Version => $"v{Mod.Version}";

        public string ButtonText
        {
            get => _buttonText;
            set { _buttonText = value; OnPropertyChanged(); }
        }

        public bool IsButtonEnabled
        {
            get => _isButtonEnabled;
            set { _isButtonEnabled = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public sealed partial class ModLibraryPage : Page
    {
        public ObservableCollection<ModLibraryItemViewModel> Mods { get; } = new ObservableCollection<ModLibraryItemViewModel>();

        public ModLibraryPage()
        {
            this.InitializeComponent();
            this.Loaded += ModLibraryPage_Loaded;
        }

        private async void ModLibraryPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadLibraryAsync();
        }

        private async Task LoadLibraryAsync()
        {
            try
            {
                using var client = new HttpClient();
                string json = await client.GetStringAsync("https://neodashweb.pages.dev/modloader/mods/modlist.json");
                
                var response = JsonSerializer.Deserialize<ModLibraryResponse>(json);
                
                if (response != null && response.Mods != null)
                {
                    LibraryTitle.Text = response.Name ?? "Mod Library";
                    LibrarySubtitle.Text = response.Subtitle ?? "Browse and install mods";
                    
                    Mods.Clear();
                    foreach (var mod in response.Mods)
                    {
                        var vm = new ModLibraryItemViewModel { Mod = mod };
                        
                        string installedVersion = SettingsManager.Values.TryGetValue($"InstalledMod_{mod.Name}", out object ivObj) ? ivObj as string : null;
                        
                        if (string.IsNullOrEmpty(installedVersion))
                        {
                            string win64Path = GamePathResolver.GetNeodashWin64Path();
                            string folderName1 = mod.Name.Replace(" ", "");
                            string folderName2 = mod.Name.Replace(" ", "_");

                            if (ModManager.IsModInstalled(win64Path, folderName1) ||
                                ModManager.IsModInstalled(win64Path, folderName2))
                            {
                                vm.ButtonText = "Installed";
                                vm.IsButtonEnabled = false;
                            }
                            else
                            {
                                vm.ButtonText = "Install";
                                vm.IsButtonEnabled = true;
                            }
                        }
                        else if (installedVersion != mod.Version)
                        {
                            vm.ButtonText = "Update";
                            vm.IsButtonEnabled = true;
                        }
                        else
                        {
                            vm.ButtonText = "Installed";
                            vm.IsButtonEnabled = false;
                        }
                        
                        Mods.Add(vm);
                    }
                    
                    ModsListView.ItemsSource = Mods;
                }
            }
            catch (Exception ex)
            {
                ErrorText.Text = "Failed to load mods: " + ex.Message;
                ErrorText.Visibility = Visibility.Visible;
                LibrarySubtitle.Visibility = Visibility.Collapsed;
            }
            finally
            {
                LoadingRing.Visibility = Visibility.Collapsed;
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            Mods.Clear();
            LoadingRing.Visibility = Visibility.Visible;
            ErrorText.Visibility = Visibility.Collapsed;
            LibrarySubtitle.Visibility = Visibility.Visible;
            LibrarySubtitle.Text = "Loading mods...";
            await LoadLibraryAsync();
        }

        private async void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ModLibraryItemViewModel vm)
            {
                vm.ButtonText = "Installing...";
                vm.IsButtonEnabled = false;

                try
                {
                    string tempZipPath = Path.Combine(Path.GetTempPath(), "lib_mod.zip");
                    using (var client = new HttpClient())
                    {
                        var response = await client.GetAsync(vm.Mod.DownloadURL);
                        response.EnsureSuccessStatusCode();

                        using (var fs = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            await response.Content.CopyToAsync(fs);
                        }
                    }

                    // Installing or updating a mod always leaves it enabled, even if it was previously disabled.
                    ModManager.InstallModZip(GamePathResolver.GetNeodashWin64Path(), tempZipPath);

                    if (File.Exists(tempZipPath)) File.Delete(tempZipPath);

                    SettingsManager.Values[$"InstalledMod_{vm.Name}"] = vm.Mod.Version;
                    SettingsManager.SaveSettings();

                    vm.ButtonText = "Installed";
                }
                catch (Exception ex)
                {
                    ErrorText.Text = "Error installing mod: " + ex.Message;
                    ErrorText.Visibility = Visibility.Visible;
                    vm.ButtonText = "Install";
                    vm.IsButtonEnabled = true;
                }
            }
        }
    }
}
