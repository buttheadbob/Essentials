using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Essentials.Conditions;
using Sandbox.Game.Entities;
using Torch.Commands;

namespace Essentials.Commands;

public static class ConditionsChecker
{
    private static List<Condition> _conditionLookup = [];

    public static void Init()
    {
        _conditionLookup = new List<Condition>();
                
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
        
    public static IEnumerable<MyCubeGrid> ScanConditions(CommandContext context, IReadOnlyList<string> args)
    {
        List<Func<MyCubeGrid, bool?>> conditions = [];

        for (int i = 0; i < args.Count; i++)
        {
            string? parameter = i + 1 >= args.Count 
                ? null 
                : args[i + 1];

            string? arg = args[i];

            if (parameter != null)
            {
                // parameter is the name of a command. Assume this command requires no parameters
                if (_conditionLookup.Any(c => parameter.Equals(c.Command, StringComparison.CurrentCultureIgnoreCase) || parameter.Equals(c.InvertCommand, StringComparison.CurrentCultureIgnoreCase)))
                    parameter = null;
                // Next string is a parameter, so pass it to the condition and skip it next loop
                else
                    i++;
            }

            bool found = false;

            foreach (Condition? condition in _conditionLookup)
            {
                if (condition is null) continue;
                
                if (arg.Equals(condition.Command, StringComparison.CurrentCultureIgnoreCase))
                {
                    conditions.Add(g => condition.Evaluate(g, parameter ?? "", false, context));
                    found = true;
                    break;
                }

                if (arg.Equals(condition.InvertCommand, StringComparison.CurrentCultureIgnoreCase))
                {
                    conditions.Add(g => condition.Evaluate(g, parameter ?? "", true, context));
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                context.Respond($"Unknown argument '{arg}'");
                return new List<MyCubeGrid>();
            }
        }

        //default scan to find grids without pilots
        if (!args.Contains("haspilot", StringComparer.CurrentCultureIgnoreCase))
            conditions.Add(g => !ConditionsImplementations.Piloted(g));

        var resultList = new List<MyCubeGrid>();

        Dictionary<long, List<MyCubeGrid>> findResult = GridFinder.GetAllGrids();
        foreach (var kvp in findResult)
        {
            bool res = true;
            foreach (MyCubeGrid cubeGrid in kvp.Value)
            {
                foreach (Func<MyCubeGrid, bool?>? condition in conditions)
                {
                    bool? result = condition?.Invoke(cubeGrid);
                    if (result == null)
                    {
                        continue;
                    }
                    
                    if (result == true)
                    {
                        continue;
                    }
                    
                    res = false;
                    break;
                }
                
                if (!res)
                {
                    break;
                }
            }
            
            if (res)
            {
                lock (resultList)
                {
                    foreach (var grid in kvp.Value)
                    {
                        resultList.Add(grid);
                    }
                }
            }
        }

        return resultList;
    }
}