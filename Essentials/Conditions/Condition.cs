using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Sandbox.Game.Entities;
using Torch.Commands;

namespace Essentials.Conditions;

public class Condition
{
    public string Command;
    public string? InvertCommand;
    public string? HelpText;
    private MethodInfo? _method;
    public readonly ParameterInfo? Parameter;

    /// <summary>
    /// True when the condition method takes the whole mechanical group (IReadOnlyList&lt;MyCubeGrid&gt;)
    /// instead of a single grid.
    /// </summary>
    public readonly bool IsGroupCondition;

    public Condition(MethodInfo evalMethod, ConditionAttribute attribute)
    {
        Command = attribute.Command;
        InvertCommand = attribute.InvertCommand;
        HelpText = attribute.HelpText;
        _method = evalMethod;

        if (_method.ReturnType != typeof(bool))
            throw new TypeLoadException("Condition does not return a bool!");

        var p = _method.GetParameters();

        if (p.Length < 1)
            throw new TypeLoadException("Condition does not accept MyCubeGrid as first parameter");

        if (p[0].ParameterType == typeof(IReadOnlyList<MyCubeGrid>))
            IsGroupCondition = true;
        else if (p[0].ParameterType != typeof(MyCubeGrid))
            throw new TypeLoadException("Condition does not accept MyCubeGrid or IReadOnlyList<MyCubeGrid> as first parameter");

        if (p.Length > 2)
            throw new TypeLoadException("Condition can only have two parameters");

        Parameter = p.Length == 1 ? null : p[1];
    }

    /// <summary>
    /// Validates and converts the argument once, before any grid is evaluated.
    /// Returns false with an error message when the argument is missing, not allowed, or cannot be parsed.
    /// </summary>
    public bool TryPrepare(string? arg, out object? value, out string? error)
    {
        value = null;
        error = null;

        if (Parameter == null)
        {
            if (string.IsNullOrEmpty(arg))
                return true;

            error = $"Condition '{Command}' does not take a value, but '{arg}' was given.";
            return false;
        }

        if (string.IsNullOrEmpty(arg))
        {
            if (Parameter.HasDefaultValue)
            {
                value = Parameter.DefaultValue;
                return true;
            }

            error = $"Condition '{Command}' requires a value ({Parameter.ParameterType.Name} {Parameter.Name}).";
            return false;
        }

        try
        {
            value = TypeDescriptor.GetConverter(Parameter.ParameterType).ConvertFromInvariantString(arg!);
            return true;
        }
        catch (Exception)
        {
            error = $"Condition '{Command}' could not read '{arg}' as {Parameter.ParameterType.Name}.";
            return false;
        }
    }

    /// <summary>
    /// Evaluates the condition for a whole mechanical group with an argument prepared by <see cref="TryPrepare"/>.
    /// Group conditions decide for the group themselves. Single-grid conditions match when any grid in the
    /// group matches, and the inverted form matches when no grid in the group matches.
    /// </summary>
    public bool EvaluateGroup(IReadOnlyList<MyCubeGrid> group, object? value, bool invert)
    {
        bool result = IsGroupCondition
            ? Invoke(group, value)
            : group.Any(grid => Invoke(grid, value));

        return result != invert;
    }

    /// <summary>
    /// Single-grid evaluation kept for plugins that use the conditions directly.
    /// Returns null when the argument is invalid.
    /// </summary>
    public bool? Evaluate(MyCubeGrid grid, string? arg, bool invert, CommandContext context)
    {
        if (!TryPrepare(arg, out object? value, out string? error))
        {
            context.Respond(error);
            return null;
        }

        return EvaluateGroup([grid], value, invert);
    }

    private bool Invoke(object target, object? value)
    {
        if (_method is null)
            return false;

        return Parameter == null
            ? (bool)_method.Invoke(null, [target])
            : (bool)_method.Invoke(null, [target, value]);
    }
}
