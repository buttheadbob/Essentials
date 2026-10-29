using System;
using System.IO;
using System.Linq;
using System.Text;
using Sandbox;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Torch.API.Managers;
using Torch.Commands;
using Torch.Commands.Permissions;
using Torch.Mod;
using Torch.Mod.Messages;
using VRage;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Game;
using VRage.ObjectBuilders;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Screens.Helpers.RadialMenuActions;
using VRage.Collections;
using VRage.Game.Entity;
using VRage.Groups;
using VRage.ObjectBuilders.Private;

namespace Essentials;

[Category("grids")]
public class GridModule : CommandModule
{
    [Command("setowner", "Sets grid ownership to the given player or ID.", "Usage: setowner <grid> <newowner>")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void SetOwner(string gridName, string playerName)
    {
        var firstArg = Context.Args.FirstOrDefault();
        Utilities.TryGetEntityByNameOrId(gridName, out IMyEntity? entity);

        if (entity is not IMyCubeGrid grid)
        {
            Context.Respond($"Grid {gridName} not found.");
            return;
        }

        var secondArg = Context.Args.ElementAtOrDefault(1);
        long identityId;
        if (!long.TryParse(playerName, out identityId))
        {
            var player = Context.Torch.CurrentSession?.Managers?.GetManager<IMultiplayerManagerBase>().GetPlayerByName(playerName);
            if (player == null)
            {
                Context.Respond($"Player {playerName} not found.");
                return;
            }
            identityId = player.IdentityId;
        }

        grid.ChangeGridOwnership(identityId, MyOwnershipShareModeEnum.Faction);
        Context.Respond($"Transferred ownership of {grid.DisplayName} to {identityId}");
    }

    [Command("ejectall", "Ejects all Players from given grid.")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void Eject(string? gridName = null) 
    {
        int ejectedPlayersCount = 0;

        // Snapshot entity list for thread safety
        List<MyEntity> entities = MyEntities.GetEntities().ToList();

        List<MyCubeGrid> grids = entities
            .OfType<MyCubeGrid>()
            .Where(g => g.Physics != null && (gridName == null || string.Equals(g.DisplayName, gridName, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (!string.IsNullOrWhiteSpace(gridName))
        {
            if (grids.Count == 0)
            {
                Context.Respond($"No grids found with name '{gridName}'.");
                return;
            }
                
            if (grids.Count > 1)
            {
                Context.Respond($"{grids.Count} grids found with name '{gridName}'.  Aborting.");
                return;
            }
                
            // Exactly one grid matches
            MyCubeGrid? grid = grids.FirstOrDefault();
            if (grid == null)
            {
                // safety check
                Context.Respond($"No grids found with name '{gridName}'.");
                return;   
            }

            List<MyCockpit> cockpits = grid.GetFatBlocks<MyCockpit>()
                .Where(c => c.Pilot != null)
                .ToList();

            foreach (MyCockpit cockpit in cockpits)
            {
                cockpit.RemovePilot();
                ejectedPlayersCount++;
            }
        }
        else
        {
            // No grid name given – eject everyone from all cockpits
            List<MyCockpit> cockpits = grids
                .SelectMany(g => g.GetFatBlocks<MyCockpit>())
                .Where(c => c.Pilot != null)
                .ToList();

            foreach (MyCockpit cockpit in cockpits)
            {
                cockpit.RemovePilot();
                ejectedPlayersCount++;
            }
        }

        Context.Respond($"Ejected {ejectedPlayersCount} player(s){(gridName != null ? $" from '{gridName}'" : "")}.");
    }

    [Command("static large", "Makes all large grids static.")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void StaticLarge()
    {
        foreach (var grid in MyEntities.GetEntities().OfType<MyCubeGrid>().Where(g => g.GridSizeEnum == MyCubeSize.Large).Where(x => x.Projector == null))
            grid.OnConvertedToStationRequest(); //Keen why do you do this to me?
    }

    [Command("stopall", "Stops all moving grids.")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void StopAll()
    {
        int count = 0;
        foreach (var grid in MyEntities.GetEntities().OfType<MyCubeGrid>().Where(x => x.Projector == null).ToList())
        {
            if (grid.Physics == null)
                continue;

            grid.Physics.ClearSpeed();
            count++;
        }

        Context.Respond($"Stopped {count} grids.");
    }

    private const string GridListGpsTag = "[Grids List]";

    [Command("list", "Lists all grids where you own the most blocks. Shows positions or GPS markers if the server admin enables the option.")]
    [Permission(MyPromoteLevel.None)]
    public void List()
    {
        if (Context.Player == null)
        {
            Context.Respond("This command can only be used in-game.");
            return;
        }

        var id = Context.Player.IdentityId;
        var config = EssentialsPlugin.Instance.Config;
        var gpsApi = MyAPIGateway.Session?.GPS;
        StringBuilder sb = new StringBuilder();

        // Replace markers from the previous run instead of adding duplicates.
        if (config.MarkerShowPosition && gpsApi != null)
        {
            foreach (var old in gpsApi.GetGpsList(id).Where(g => g.Description?.StartsWith(GridListGpsTag) == true).ToList())
                gpsApi.RemoveGps(id, old);
        }

        foreach (var grid in MyEntities.GetEntities().OfType<MyCubeGrid>().ToList())
        {
            if (grid.Projector != null || !grid.BigOwners.Contains(id))
                continue;

            sb.AppendLine($"{grid.DisplayName} - {grid.GridSizeEnum} - {grid.BlocksCount} blocks - Position {(config.UtilityShowPosition ? grid.PositionComp.GetPosition().ToString() : "Unknown")}");
            if (config.MarkerShowPosition && gpsApi != null)
            {
                IMyGps gridGPS = gpsApi.Create(grid.DisplayName, $"{GridListGpsTag} {grid.DisplayName} - {grid.GridSizeEnum} - {grid.BlocksCount} blocks", grid.PositionComp.GetPosition(), true);
                gpsApi.AddGps(id, gridGPS);
            }
        }

        if (sb.Length == 0)
            sb.AppendLine("You don't own any grids.");

        ModCommunication.SendMessageTo(new DialogMessage("Grids List", $"Ships/Stations owned by {Context.Player.DisplayName}", sb.ToString()), Context.Player.SteamUserId);
    }

    private const string ExportPath = "ExportedGrids\\{0}.xml";

    [Command("export", "Export the given grid to the given file name.")]
    public void Export(string gridName, string exportName)
    {
        Directory.CreateDirectory("ExportedGrids");
        if (!Utilities.TryGetEntityByNameOrId(gridName, out var ent) || ent is not IMyCubeGrid)
        {
            Context.Respond("Grid not found.");
            return;
        }

        var path = string.Format(ExportPath, exportName);
        if (File.Exists(path))
        {
            Context.Respond("Export file already exists.");
            return;
        }

        MyObjectBuilderSerializerKeen.SerializeXML(path, false, ent.GetObjectBuilder());
        Context.Respond($"Grid saved to {path}");
    }
        
    [Command("import", "Import a grid from file and spawn it by the given entity/player.")]
    public void Import(string gridName, string? targetName = null)
    {
        Directory.CreateDirectory("ExportedGrids");
        if (targetName == null)
        {
            if (Context.Player == null)
            {
                Context.Respond("Target entity must be specified.");
                return;   
            }

        }

        IMyEntity? ent;
        if (targetName == null)
        {
            ent = Context.Player!.Controller?.ControlledEntity?.Entity;
            if (ent == null || Context.Player.Character == null || Context.Player.Character.IsDead)
            {
                Context.Respond("You must be alive in the world to spawn a grid next to you.");
                return;
            }
        }
        else if (!Utilities.TryGetEntityByNameOrId(targetName, out ent))
        {
            Context.Respond("Target entity not found.");
            return;
        }

        var path = string.Format(ExportPath, gridName);
        if (!File.Exists(path))
        {
            Context.Respond("File does not exist.");
            return;
        }

        if (MyObjectBuilderSerializerKeen.DeserializeXML(path, out MyObjectBuilder_CubeGrid grid))
        {
            Context.Respond($"Importing grid from {path}");
            MyEntities.RemapObjectBuilder(grid);
            var pos = MyEntities.FindFreePlaceCustom(ent!.GetPosition(), grid.CalculateBoundingSphere().Radius);
            if (pos == null)
            {
                Context.Respond("No free place.");
                return;
            }

            var x = grid.PositionAndOrientation ?? new MyPositionAndOrientation();
            x.Position = pos.Value;
            grid.PositionAndOrientation = x;
            MyEntities.CreateFromObjectBuilderParallel(grid, true);
        }
    }
}