using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Neodash_Mod_Loader.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Neodash_Mod_Loader.Services;

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
    public Visibility ActionsVisibility { get; set; } = Visibility.Collapsed;
    public bool CanRepair { get; set; }

    public string ButtonText
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
        }
    } = "Install";

    public bool IsButtonEnabled
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
        }
    } = true;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
