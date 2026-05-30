using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Torch.Commands;

namespace Essentials.Commands;

[Category("blocks")]
public class BlocksModule : CommandModule
{
    private static readonly Logger Log = LogManager.GetLogger("Essentials");

    [Command("on", "Turn on blocks matching the given conditions. Usage: !blocks on [type|subtype|general] <value> [conditions...]")]
    public void On()
    {
        if (Context.Args.Count == 0)
        {
            Context.Respond("Usage: !blocks on [type|subtype|general] <value> [conditions...]");
            Context.Respond("Examples:");
            Context.Respond("  !blocks on type MyObjectBuilder_Reactor");
            Context.Respond("  !blocks on subtype LargeBlockSmallGenerator");
            Context.Respond("  !blocks on general power");
            Context.Respond("  !blocks on type MyObjectBuilder_BatteryBlock ownedby PlayerName");
            return;
        }

        ProcessBlockCommand(BlockAction.TurnOn);
    }

    [Command("off", "Turn off blocks matching the given conditions. Usage: !blocks off [type|subtype|general] <value> [conditions...]")]
    public void Off()
    {
        if (Context.Args.Count == 0)
        {
            Context.Respond("Usage: !blocks off [type|subtype|general] <value> [conditions...]");
            Context.Respond("Examples:");
            Context.Respond("  !blocks off type MyObjectBuilder_Reactor");
            Context.Respond("  !blocks off subtype LargeBlockSmallGenerator");
            Context.Respond("  !blocks off general weapons");
            Context.Respond("  !blocks off type MyObjectBuilder_Turret playerdistancegreaterthan 5000");
            return;
        }

        ProcessBlockCommand(BlockAction.TurnOff);
    }

    [Command("remove", "Remove blocks matching the given conditions. Usage: !blocks remove [type|subtype] <value> [conditions...]")]
    public void Remove()
    {
        if (Context.Args.Count == 0)
        {
            Context.Respond("Usage: !blocks remove [type|subtype] <value> [conditions...]");
            Context.Respond("Examples:");
            Context.Respond("  !blocks remove type MyObjectBuilder_Beacon");
            Context.Respond("  !blocks remove subtype LargeBlockBeacon");
            Context.Respond("  !blocks remove type MyObjectBuilder_Beacon ownedby nobody");
            return;
        }

        ProcessBlockCommand(BlockAction.Remove);
    }

    [Command("count", "Count blocks matching the given conditions. Usage: !blocks count [type|subtype|general] <value> [conditions...]")]
    public void Count()
    {
        if (Context.Args.Count == 0)
        {
            Context.Respond("Usage: !blocks count [type|subtype|general] <value> [conditions...]");
            Context.Respond("Examples:");
            Context.Respond("  !blocks count type MyObjectBuilder_Reactor");
            Context.Respond("  !blocks count general production");
            return;
        }

        ProcessBlockCommand(BlockAction.Count);
    }

    #region Core Processing Logic

    private enum BlockAction
    {
        TurnOn,
        TurnOff,
        Remove,
        Count
    }

    private void ProcessBlockCommand(BlockAction action)
    {
        if (Context.Args.Count < 2)
        {
            Context.Respond("Not enough arguments. Specify a selector (type/subtype/general) and value.");
            return;
        }

        var selector = Context.Args[0].ToLower();
        var value = Context.Args[1];

        // Parse remaining args as conditions
        var conditionArgs = Context.Args.Skip(2).ToList();
            
        // Get grids matching conditions (if any)
        IEnumerable<MyCubeGrid> grids;
        grids = conditionArgs.Count > 0 
            ? ConditionsChecker.ScanConditions(Context, conditionArgs) 
            : MyEntities.GetEntities().OfType<MyCubeGrid>().Where(x => x.Projector == null);

        var gridsList = grids.ToList();
        if (gridsList.Count == 0)
        {
            Context.Respond("No grids found matching the specified conditions.");
            return;
        }

        int processedCount = 0;

        switch (selector)
        {
            case "type":
                processedCount = ProcessByType(gridsList, value, action);
                break;
            case "subtype":
                processedCount = ProcessBySubtype(gridsList, value, action);
                break;
            case "general":
            case "category":
                processedCount = ProcessByCategory(gridsList, value, action);
                break;
            default:
                Context.Respond($"Invalid selector '{selector}'. Use 'type', 'subtype', or 'general'.");
                return;
        }

        ReportResult(action, processedCount, selector, value);
    }

    private int ProcessByType(List<MyCubeGrid> grids, string type, BlockAction action)
    {
        int count = 0;

        if (action == BlockAction.Remove)
        {
            var toRemove = new List<MySlimBlock>();
            foreach (var grid in grids)
            {
                foreach (var block in grid.GetBlocks())
                {
                    var blockType = block.BlockDefinition.Id.TypeId.ToString().Substring(16);
                    if (string.Compare(type, blockType, StringComparison.InvariantCultureIgnoreCase) == 0)
                        toRemove.Add(block);
                }
            }

            foreach (var block in toRemove)
            {
                block.CubeGrid?.RemoveBlock(block);
                count++;
            }
        }
        else
        {
            foreach (var grid in grids)
            {
                foreach (var block in grid.GetFatBlocks().OfType<MyFunctionalBlock>())
                {
                    var blockType = block.BlockDefinition.Id.TypeId.ToString().Substring(16);
                    if (string.Compare(type, blockType, StringComparison.InvariantCultureIgnoreCase) == 0)
                    {
                        if (ExecuteAction(block, action))
                            count++;
                    }
                }
            }
        }

        return count;
    }

    private int ProcessBySubtype(List<MyCubeGrid> grids, string subtype, BlockAction action)
    {
        int count = 0;

        if (action == BlockAction.Remove)
        {
            var toRemove = new List<MySlimBlock>();
            foreach (var grid in grids)
            {
                foreach (var block in grid.GetBlocks())
                {
                    var blockSubtype = block.BlockDefinition.Id.SubtypeName;
                    if (string.Compare(subtype, blockSubtype, StringComparison.InvariantCultureIgnoreCase) == 0)
                        toRemove.Add(block);
                }
            }

            foreach (var block in toRemove)
            {
                block.CubeGrid?.RemoveBlock(block);
                count++;
            }
        }
        else
        {
            foreach (var grid in grids)
            {
                foreach (var block in grid.GetFatBlocks().OfType<MyFunctionalBlock>())
                {
                    var blockSubtype = block.BlockDefinition.Id.SubtypeName;
                    if (string.Compare(subtype, blockSubtype, StringComparison.InvariantCultureIgnoreCase) == 0)
                    {
                        if (ExecuteAction(block, action))
                            count++;
                    }
                }
            }
        }

        return count;
    }

    private int ProcessByCategory(List<MyCubeGrid> grids, string category, BlockAction action)
    {
        if (!Enum.TryParse(category, true, out BlockCategory blockCategory))
        {
            Context.Respond($"{category} is not a valid category. Use one of the following: " + 
                            string.Join(", ", Enum.GetValues(typeof(BlockCategory))));
            return 0;
        }

        if (action == BlockAction.Remove)
        {
            Context.Respond("Remove action is not supported for categories. Use type or subtype instead.");
            return 0;
        }

        int count = 0;
        foreach (var grid in grids)
        {
            foreach (var block in grid.GetFatBlocks().OfType<MyFunctionalBlock>())
            {
                if (IsBlockTypeOf(block, blockCategory))
                {
                    if (ExecuteAction(block, action))
                        count++;
                }
            }
        }

        return count;
    }

    private bool ExecuteAction(MyFunctionalBlock block, BlockAction action)
    {
        switch (action)
        {
            case BlockAction.TurnOn:
                if (!block.Enabled)
                {
                    block.Enabled = true;
                    return true;
                }
                break;
            case BlockAction.TurnOff:
                if (block.Enabled)
                {
                    block.Enabled = false;
                    return true;
                }
                break;
            case BlockAction.Count:
                return true;
        }
        return false;
    }

    private void ReportResult(BlockAction action, int count, string selector, string value)
    {
        string actionText;
        switch (action)
        {
            case BlockAction.TurnOn:
                actionText = "Enabled";
                break;
            case BlockAction.TurnOff:
                actionText = "Disabled";
                break;
            case BlockAction.Remove:
                actionText = "Removed";
                break;
            case BlockAction.Count:
                actionText = "Found";
                break;
            default:
                actionText = "Processed";
                break;
        }

        Context.Respond($"{actionText} {count} blocks of {selector} {value}.");
        Log.Info($"BlocksModule: {actionText} {count} blocks of {selector} {value}");
    }

    #endregion

    #region Helper Methods

    public bool IsBlockTypeOf(MyFunctionalBlock block, BlockCategory category)
    {
        switch (category)
        {
            case BlockCategory.Power:
                return block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_Reactor) ||
                       block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_BatteryBlock) ||
                       block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_SolarPanel) ||
                       block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_FueledPowerProducer);
            case BlockCategory.Production:
                return block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_Assembler) ||
                       block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_Refinery) ||
                       block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_OxygenGenerator);
            case BlockCategory.Weapons:
                return block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_InteriorTurret) ||
                       block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_LargeGatlingTurret) ||
                       block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_LargeMissileTurret);
            default:
                throw new InvalidBranchException();
        }
    }

    public enum BlockCategory
    {
        Power,
        Production,
        Weapons
    }

    #endregion
}