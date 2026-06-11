using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;
using Torch;

namespace Essentials;

public class BlockList : ViewModel
{
    public string Name { get; set => SetValue(ref field, value); } = "";

    public BlockAction Action { get; set => SetValue(ref field, value); }

    public ObservableCollection<BlockListItem> Items { get; } = [];

    public ObservableCollection<BlockListCondition> Conditions { get; } = [];

    public BlockList()
    {
        WatchSubCollection(Items);
        WatchSubCollection(Conditions);
    }

    public override string ToString() => $"{Name} ({Action}) - {Items.Count} items";

    public string GetCommand(string action, BlockListItem item)
    {
        var sb = new StringBuilder();
        sb.Append($"!blocks {action} {item.Selector.ToString().ToLower()} {item.Value}");
        foreach (var condition in Conditions)
        {
            sb.Append($" {condition.Condition.ToCommandString()}");
            if (!string.IsNullOrWhiteSpace(condition.Parameter))
                sb.Append($" {condition.Parameter}");
        }
        return sb.ToString();
    }

    private void WatchSubCollection<T>(ObservableCollection<T> collection) where T : ViewModel
    {
        collection.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
                foreach (T item in e.NewItems)
                    item.PropertyChanged += SubItemChanged;
            if (e.OldItems != null)
                foreach (T item in e.OldItems)
                    item.PropertyChanged -= SubItemChanged;
            OnPropertyChanged();
        };

        foreach (var item in collection)
            item.PropertyChanged += SubItemChanged;
    }

    private void SubItemChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged();
}

public class BlockListItem : ViewModel
{
    public SelectorType Selector { get; set => SetValue(ref field, value); }

    public string Value { get; set => SetValue(ref field, value); } = "";

    public override string ToString() => $"{Selector}: {Value}";
}

public class BlockListCondition : ViewModel
{
    public ConditionType Condition { get; set => SetValue(ref field, value); }

    public string? Parameter { get; set => SetValue(ref field, value); }

    public override string ToString() => string.IsNullOrWhiteSpace(Parameter) ? $"{Condition}" : $"{Condition}: {Parameter}";
}

public enum BlockAction
{
    TurnOn,
    TurnOff,
    Remove
}

public enum SelectorType
{
    Type,
    Subtype
}

public enum ConditionType
{
    Name,
    BlocksLessThan,
    BlocksGreaterThan,
    PcuGreaterThan,
    PcuLessThan,
    HasGridType,
    HasOwnerType,
    HasPower,
    NoPower,
    InsidePlanet,
    PlayerDistanceLessThan,
    PlayerDistanceGreaterThan,
    PoweredGridDistanceGreaterThan,
    CenterDistanceLessThan,
    CenterDistanceGreaterThan,
    OwnedBy,
    HasType,
    NoType,
    HasTypeFast,
    NoTypeFast,
    HasSubtype,
    NoSubtype,
    HasSubtypeFast,
    NoSubtypeFast,
    HasPilot
}

public static class ConditionTypeExtensions
{
    public static string ToCommandString(this ConditionType condition)
    {
        return condition switch
        {
            ConditionType.HasTypeFast => "hastype-fast",
            ConditionType.NoTypeFast => "notype-fast",
            ConditionType.HasSubtypeFast => "hassubtype-fast",
            ConditionType.NoSubtypeFast => "nosubtype-fast",
            _ => condition.ToString().ToLower()
        };
    }
}
