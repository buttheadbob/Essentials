using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using Torch.Views;
using VRage;
using VRage.Game;
using VRage.ObjectBuilders;

namespace Essentials;

public class EssentialsConfig : ViewModel
{
    public EssentialsConfig()
    {
        AutoCommands.CollectionChanged += (sender, args) => OnPropertyChanged();
        InfoCommands.CollectionChanged += (sender, args) => OnPropertyChanged();
    }

    [Display(EditorType = typeof(EmbeddedCollectionEditor))]
    public ObservableCollection<AutoCommand> AutoCommands { get; } = [];

    [Display(EditorType = typeof(EmbeddedCollectionEditor))]
    public ObservableCollection<InfoCommand> InfoCommands { get; } = [];

    [Display(Name = "Motd", Description = "Message displayed to players upon connection")]
    public string Motd { get; set => SetValue(ref field, value); }

    public string NewUserMotd { get; set => SetValue(ref field, value); }

    [Display(Name = "MotdURL", Description = "Sets a URL to show to players when they connect. Opens in the steam overlay, if enabled.")]
    public string MotdUrl { get; set => SetValue(ref field, value); }

    [Display(Name = "Url for New Users Only", Description = "MOTD URL for new users only")] 
    public bool NewUserMotdUrl { get; set => SetValue(ref field, value); }

    [Display(Name = "Stop entities on start", Description = "Stop all entities in the world when the server starts.")]
    public bool StopShipsOnStart { get; set => SetValue(ref field, value); }


    [Display(Name = "Grid list show position", Description = "Show users the position of all grids they own in the grids list command.")]
    public bool UtilityShowPosition { get; set => SetValue(ref field, value); }

    [Display(Name = "Grid list GPS marker", Description = "Show uservers the poition of all grids they own by gps marker")]
    public bool MarkerShowPosition { get; set => SetValue(ref field, value); }

    [Display(Name = "Backpack Limit", Description = "Sets the number of backpacks that can belong to any player. Empty backpacks are deleted after 30 seconds, and backpacks which break the limit are deleted in order spawned. Set -1 for no limit.")]
    public int BackpackLimit { get; set => SetValue(ref field, value); } = 1;

    [Display(Name = "Cut Game Tags", GroupName = "Client Join Tweaks", Order = 8, Description = "Cuts mods and blocks limits from matchmaking server info. Prevents from 'error downloading session settings'.")]
    public bool CutGameTags { get; set => SetValue(ref field, value); }

    [XmlIgnore]
    private MyObjectBuilder_Toolbar VanillaDefaultToolbar => field ??= new MyToolbar(MyToolbarType.Character).GetObjectBuilder();

    private MyObjectBuilder_Toolbar? _defaultToolbar;

    [Display(Visible=false)]
    //TODO!
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

    /// <summary>
    /// Allows us to use Keen's serializer without losing previously stored config data
    /// </summary>
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