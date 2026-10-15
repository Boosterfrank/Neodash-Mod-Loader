using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Neodash_Mod_Loader.Services;

public sealed partial class EditableMod(InstalledModState state) : INotifyPropertyChanged
{
    public string Name => state.Name;
    public bool CanRepair => state.CanRepair;
    public bool CanRestore => state.CanRestore;
    public string ManagedVersion => state.ManagedVersion;
    public bool OriginalIsEnabled { get; internal set; } = state.IsEnabled;

    public bool IsEnabled
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
            }
        }
    } = state.IsEnabled;

    public string EntryPointPath
    {
        get;
        internal set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
            }
        }
    } = state.EntryPointPath;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
