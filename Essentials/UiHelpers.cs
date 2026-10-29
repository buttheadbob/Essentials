using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Essentials;

/// <summary>
/// Rejects text that is not a valid HH:MM:SS time, so the field shows an error instead of silently keeping
/// the old value.
/// </summary>
public class TimeSpanValidationRule : ValidationRule
{
    public override ValidationResult Validate(object value, CultureInfo cultureInfo)
    {
        return TimeSpan.TryParse(value as string, CultureInfo.InvariantCulture, out var span) && span >= TimeSpan.Zero
            ? ValidationResult.ValidResult
            : new ValidationResult(false, "Enter a time as HH:MM:SS, for example 00:05:00.");
    }
}

/// <summary>
/// Binding for HH:MM:SS text fields: updates on lost focus and validates the format.
/// Usage: Text="{local:TimeSpanBinding Interval}"
/// </summary>
public class TimeSpanBinding : Binding
{
    public TimeSpanBinding()
    {
        Initialize();
    }

    public TimeSpanBinding(string path) : base(path)
    {
        Initialize();
    }

    private void Initialize()
    {
        UpdateSourceTrigger = UpdateSourceTrigger.LostFocus;
        ValidationRules.Add(new TimeSpanValidationRule());
    }
}

/// <summary>
/// Visible when the bound Trigger is one of the comma-separated triggers in the converter parameter.
/// </summary>
public class TriggerVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not Trigger trigger || parameter is not string names)
            return Visibility.Collapsed;

        return names.Split(',').Any(n => string.Equals(n.Trim(), trigger.ToString(), StringComparison.OrdinalIgnoreCase))
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>
/// Short explanation of what an Auto Command trigger does.
/// </summary>
public class TriggerDescriptionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            Trigger.Disabled => "This command never runs on its own. You can still run it with !admin runauto <name>.",
            Trigger.Timed => "Runs the steps over and over, waiting the Repeat Every time between runs.",
            Trigger.Scheduled => "Runs the steps once a day at the Time Of Day (server time), on the chosen day or every day.",
            Trigger.OnStart => "Runs the steps once, shortly after the server has finished starting.",
            Trigger.GridCount => "Checks the number of grids every Check Every interval and runs the steps when the comparison is true. It will not start again while a previous run is still going.",
            Trigger.PlayerCount => "Checks the number of online players every Check Every interval and runs the steps when the comparison is true. It will not start again while a previous run is still going.",
            Trigger.Vote => "Players start a vote with !vote <name>. If enough online players type !yes before the vote ends, the steps run.",
            _ => ""
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
