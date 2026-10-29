using System.Collections.ObjectModel;
using System.ComponentModel;
using Torch;

namespace Essentials;

public class EssentialsConfig : ViewModel
{
    public EssentialsConfig()
    {
        WatchCollection(AutoCommands);
        WatchCollection(InfoCommands);
        WatchCollection(BlockLists);
        WatchCollection(CleanupPresets);
        WatchCollection(PlayerPermissions);
    }

    public ObservableCollection<AutoCommand> AutoCommands { get; } = [];

    public ObservableCollection<InfoCommand> InfoCommands { get; } = [];

    public ObservableCollection<BlockList> BlockLists { get; } = [];

    public ObservableCollection<CleanupPreset> CleanupPresets { get; } = [];

    public bool EnableCommandPermissions { get; set => SetValue(ref field, value); }

    public ObservableCollection<PlayerPermission> PlayerPermissions { get; } = [];

    public string Motd { get; set => SetValue(ref field, value); } = "";

    public string NewUserMotd { get; set => SetValue(ref field, value); } = "";

    public string MotdUrl { get; set => SetValue(ref field, value); } = "";

    public bool NewUserMotdUrl { get; set => SetValue(ref field, value); }

    public bool StopShipsOnStart { get; set => SetValue(ref field, value); }

    public bool UtilityShowPosition { get; set => SetValue(ref field, value); }

    public bool MarkerShowPosition { get; set => SetValue(ref field, value); }

    public int BackpackLimit { get; set => SetValue(ref field, value); } = 1;

    public bool CutGameTags { get; set => SetValue(ref field, value); }

    private void WatchCollection<T>(ObservableCollection<T> collection) where T : ViewModel
    {
        collection.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
                foreach (T item in e.NewItems)
                    item.PropertyChanged += ItemChanged;
            if (e.OldItems != null)
                foreach (T item in e.OldItems)
                    item.PropertyChanged -= ItemChanged;
            OnPropertyChanged();
        };

        foreach (var item in collection)
            item.PropertyChanged += ItemChanged;
    }

    private void ItemChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged();
}
