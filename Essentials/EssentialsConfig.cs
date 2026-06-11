using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using Sandbox.Game.Screens.Helpers;
using Torch;
using VRage;
using VRage.Game;
using VRage.ObjectBuilders;

namespace Essentials;

public class EssentialsConfig : ViewModel
{
    public EssentialsConfig()
    {
        WatchCollection(AutoCommands);
        WatchCollection(InfoCommands);
        WatchCollection(BlockLists);
    }

    public ObservableCollection<AutoCommand> AutoCommands { get; } = [];

    public ObservableCollection<InfoCommand> InfoCommands { get; } = [];

    public ObservableCollection<BlockList> BlockLists { get; } = [];

    public string Motd { get; set => SetValue(ref field, value); } = "";

    public string NewUserMotd { get; set => SetValue(ref field, value); } = "";

    public string MotdUrl { get; set => SetValue(ref field, value); } = "";

    public bool NewUserMotdUrl { get; set => SetValue(ref field, value); }

    public bool StopShipsOnStart { get; set => SetValue(ref field, value); }

    public bool UtilityShowPosition { get; set => SetValue(ref field, value); }

    public bool MarkerShowPosition { get; set => SetValue(ref field, value); }

    public int BackpackLimit { get; set => SetValue(ref field, value); } = 1;

    public bool CutGameTags { get; set => SetValue(ref field, value); }

    [XmlIgnore]
    private MyObjectBuilder_Toolbar VanillaDefaultToolbar => field ??= new MyToolbar(MyToolbarType.Character).GetObjectBuilder();

    private MyObjectBuilder_Toolbar? _defaultToolbar;

    public ToolbarWrapper DefaultToolbar
    {
        get => _defaultToolbar ?? VanillaDefaultToolbar;
        set
        {
            bool valueChanged = false;

            if (value.Data?.Slots.Count == VanillaDefaultToolbar.Slots.Count)
            {
                for (int i = 0; i < value.Data.Slots.Count; i++)
                {
                    var val = value.Data.Slots[i];
                    var van = VanillaDefaultToolbar.Slots[i];
                    if (val.Index != van.Index || val.Data.SubtypeId != van.Data.SubtypeId)
                    {
                        valueChanged = true;
                        break;
                    }
                }
            }
                
            if (valueChanged)
                SetValue(ref _defaultToolbar, value);
        }
    }

    public bool ShouldSerializeDefaultToolbar()
    {
        return _defaultToolbar != null;
    }

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

    public class ToolbarWrapper : IXmlSerializable
    {
        public MyObjectBuilder_Toolbar? Data { get; set; }

        public XmlSchema? GetSchema()
        {
            return null;
        }

        public void ReadXml(XmlReader reader)
        {
            var ser = MyXmlSerializerManager.GetSerializer(typeof(MyObjectBuilder_Toolbar));
            var o = ser.Deserialize(reader);
            Data = (MyObjectBuilder_Toolbar)o;
        }

        public void WriteXml(XmlWriter writer)
        {
            if (Data is null) return;
            var ser = MyXmlSerializerManager.GetSerializer(typeof(MyObjectBuilder_Toolbar));
            ser.Serialize(writer, Data);
        }

        public static implicit operator MyObjectBuilder_Toolbar(ToolbarWrapper o)
        {
            if (o.Data is null)
                return new MyObjectBuilder_Toolbar();
                
            return o.Data;
        }

        public static implicit operator ToolbarWrapper(MyObjectBuilder_Toolbar o)
        {
            return new ToolbarWrapper(){Data = o};
        }
    }
}
