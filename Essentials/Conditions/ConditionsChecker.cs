using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Essentials.Conditions;
using NLog;
using Sandbox.Game.Entities;
using Torch.Commands;

namespace Essentials.Commands;

public static class ConditionsChecker
{
    private static readonly Logger Log = LogManager.GetLogger("Essentials");
    private static List<Condition> _conditionLookup = [];

    public static void Init()
    {
        _conditionLookup.Clear();

        var types = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany((x) =>
            {
                try
                {
                    return x.GetTypes();
                }
                catch (Exception) // ignored
                {
                    return Type.EmptyTypes;
                }
            }).Where(t => t.IsDefined(typeof(ConditionModule)));

        foreach (var type in types)
        {
            var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            foreach (var m in methods)
            {
                var a = m.GetCustomAttribute<ConditionAttribute>();
                if (a == null)
                    continue;

                var c = new Condition(m, a);

                _conditionLookup.Add(c);
            }
        }
    }

    public static List<Condition> GetAllConditions()
    {
        return _conditionLookup;
    }

    /// <summary>
    /// Returns every grid of each mechanical group that matches all given conditions.
    /// Any unknown condition or invalid value aborts the scan and returns no grids.
    /// </summary>
    public static IEnumerable<MyCubeGrid> ScanConditions(CommandContext context, IReadOnlyList<string> args)
    {
        var conditions = new List<Func<IReadOnlyList<MyCubeGrid>, bool>>();

        for (var i = 0; i < args.Count; i++)
        {
            string? parameter;
            parameter = i + 1 >= args.Count
                ? null
                : args[i + 1];

            var arg = args[i];

            if (parameter != null)
            {
                //parameter is the name of a command. Assume this command requires no parameters
                if (_conditionLookup.Any(c => parameter.Equals(c.Command, StringComparison.CurrentCultureIgnoreCase) || parameter.Equals(c.InvertCommand, StringComparison.CurrentCultureIgnoreCase)))
                {
                    parameter = null;
                }
                //next string is a parameter, so pass it to the condition and skip it next loop
                else
                    i++;
            }

            var condition = _conditionLookup.FirstOrDefault(c => arg.Equals(c.Command, StringComparison.CurrentCultureIgnoreCase));
            var invert = false;
            if (condition == null)
            {
                condition = _conditionLookup.FirstOrDefault(c => arg.Equals(c.InvertCommand, StringComparison.CurrentCultureIgnoreCase));
                invert = true;
            }

            if (condition == null)
            {
                context.Respond($"Unknown condition '{arg}'. Nothing was matched.");
                return new List<MyCubeGrid>();
            }

            if (!condition.TryPrepare(parameter, out object? value, out string? error))
            {
                context.Respond($"{error} Nothing was matched.");
                return new List<MyCubeGrid>();
            }

            conditions.Add(group => condition.EvaluateGroup(group, value, invert));
        }

        //default scan to find grids without pilots
        if (!args.Contains("haspilot", StringComparer.CurrentCultureIgnoreCase))
            conditions.Add(group => !group.Any(ConditionsImplementations.Piloted));

        var resultList = new List<MyCubeGrid>();

        try
        {
            foreach (var group in GridFinder.GetAllGrids().Values)
            {
                if (conditions.All(condition => condition(group)))
                    resultList.AddRange(group);
            }
        }
        catch (Exception e)
        {
            var reason = e.GetBaseException().Message;
            Log.Error(e, "Condition evaluation failed");
            context.Respond($"Condition check failed: {reason} Nothing was matched.");
            return new List<MyCubeGrid>();
        }

        return resultList;
    }
}
