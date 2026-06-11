using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using Torch;

namespace Essentials;

public class CleanupPreset : ViewModel
{
    public string Name { get; set => SetValue(ref field, value); } = "";

    public CleanupAction Action { get; set => SetValue(ref field, value); }

    public bool ApplyToAll { get; set => SetValue(ref field, value); }

    public ObservableCollection<CleanupTarget> Targets { get; } = [];

    public ObservableCollection<CleanupCondition> Conditions { get; } = [];

    public CleanupPreset()
    {
        WatchSubCollection(Targets);
        WatchSubCollection(Conditions);
    }

    public override string ToString() => $"{Name} ({Action})";

    public List<string> GetCommands()
    {
        if (Action == CleanupAction.DeleteFloatingObjects)
            return ["!cleanup delete floatingobjects"];

        if (ApplyToAll)
            return [BuildCommand(null)];

        return Targets.Select(t => BuildCommand(t)).ToList();
    }

    internal string BuildCommand(CleanupTarget? target)
    {
        var sb = new StringBuilder("!cleanup delete");
        if (target != null)
            AppendCondition(sb, target.Condition, target.Parameter);
        foreach (var c in Conditions)
            AppendCondition(sb, c.Condition, c.Parameter);
        return sb.ToString();
    }

    private static void AppendCondition(StringBuilder sb, CleanupConditionType type, string? param)
    {
        sb.Append(' ');
        sb.Append(type.ToCommandString());
        if (!string.IsNullOrWhiteSpace(param))
        {
            sb.Append(' ');
            if (type == CleanupConditionType.Name || param.Contains(' '))
                sb.Append($"\"{param}\"");
            else
                sb.Append(param);
        }
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

public class CleanupTarget : ViewModel
{
    public CleanupConditionType Condition { get; set => SetValue(ref field, value); }

    public string? Parameter { get; set => SetValue(ref field, value); }

    internal TimeSpan DelaySpan;

    public string Delay
    {
        get => DelaySpan.ToString();
        set => SetValue(ref DelaySpan, TimeSpan.Parse(value));
    }

    public override string ToString() => string.IsNullOrWhiteSpace(Parameter) ? $"{Condition}" : $"{Condition}: {Parameter}";
}

public class CleanupCondition : ViewModel
{
    public CleanupConditionType Condition { get; set => SetValue(ref field, value); }

    public string? Parameter { get; set => SetValue(ref field, value); }

    public override string ToString() => string.IsNullOrWhiteSpace(Parameter) ? $"{Condition}" : $"{Condition}: {Parameter}";
}

public enum CleanupAction
{
    Delete,
    DeleteFloatingObjects
}

public enum CleanupConditionType
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

public static class CleanupConditionTypeExtensions
{
    public static string ToCommandString(this CleanupConditionType condition)
    {
        return condition switch
        {
            CleanupConditionType.HasTypeFast => "hastype-fast",
            CleanupConditionType.NoTypeFast => "notype-fast",
            CleanupConditionType.HasSubtypeFast => "hassubtype-fast",
            CleanupConditionType.NoSubtypeFast => "nosubtype-fast",
            _ => condition.ToString().ToLower()
        };
    }
}
