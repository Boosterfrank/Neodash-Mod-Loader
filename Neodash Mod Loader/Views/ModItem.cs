using Microsoft.UI.Xaml;
using Neodash_Mod_Loader.Services;
using System.ComponentModel;

namespace Neodash_Mod_Loader.Views;

/// <summary>XAML adapter for the window-scoped editable mod state.</summary>
public sealed partial class ModItem : INotifyPropertyChanged, IDisposable
{
    private readonly EditableMod _mod;
    public ModItem(EditableMod mod)
    {
        _mod = mod;
        _mod.PropertyChanged += Mod_PropertyChanged;
    }

    public EditableMod Model => _mod;
    public string Name => _mod.Name;
    public bool CanRepair => _mod.CanRepair;
    public bool CanRestore => _mod.CanRestore;
    public string ManagedVersion => _mod.ManagedVersion;
    public Visibility ManagedVersionVisibility => string.IsNullOrWhiteSpace(ManagedVersion) ? Visibility.Collapsed : Visibility.Visible;
    public string EntryPointPath => _mod.EntryPointPath;
    public bool IsEnabled { get => _mod.IsEnabled; set => _mod.IsEnabled = value; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Mod_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        PropertyChanged?.Invoke(this, e);
    }

    public void Dispose() => _mod.PropertyChanged -= Mod_PropertyChanged;
}
