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
using System.Collections.Concurrent;
using System.Collections.Generic;
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

        string? secondArg = Context.Args.ElementAtOrDefault(1);
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

        /*
        grid.GetBlocks(new List<IMySlimBlock>(), block =>
        {
            var cubeBlock = block.FatBlock as MyCubeBlock;
            var ownerComp = cubeBlock?.Components.Get<MyEntityOwnershipComponent>();
            if (ownerComp == null)
                return false;

            cubeBlock?.ChangeOwner(0, MyOwnershipShareModeEnum.All);
            cubeBlock?.ChangeOwner(identityId, ownerComp.ShareMode);
            return false;
        });*/
    }

    [Command("ejectall", "Ejects all Players from given grid.")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void Eject(string? gridName = null) 
    {
        (long, List<MyCubeGrid>) gridGroups;

        if (string.IsNullOrWhiteSpace(gridName)) 
        {
            if(Context.Player is null) 
            {
                Context.Respond("The console always has to pass a grid name or id!");
                return;
            }

            IMyCharacter character = Context.Player.Character;

            if (character == null) 
            {
                Context.Respond("You need to spawn into a character when not using a grid name or id!");
                return;
            }

            gridGroups = GridFinder.FindLookAtGridGroupMechanical(character);

            if (gridGroups.Item2.Count == 0) 
            {
                Context.Respond("No grid in your line of sight found! Remember to NOT use spectator!");
                return;
            }
        } 
        else 
        {
            gridGroups = GridFinder.FindGridGroupMechanical(gridName!);

            if (gridGroups.Item2.Count == 0) 
            {
                Context.Respond($"Grid with name/id '{gridName}' was not found or multiple grids were found!");
                return;
            }
        }

        
        int ejectedPlayersCount = 0;

        foreach(var grid in gridGroups.Item2) 
        {
            foreach(var fatBlock in grid.GetFatBlocks()) 
            {
                if (fatBlock is not MyShipController shipController || shipController.Pilot == null)
                    continue;
                
                shipController.Use();
                ejectedPlayersCount++;
            }
        }

        Context.Respond($"Ejected '{ejectedPlayersCount}' players from their seats.");
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
        foreach (var grid in MyEntities.GetEntities().OfType<MyCubeGrid>().Where(x => x.Projector == null))
        {
            grid.Physics.ClearSpeed();
        }
    }

    [Command("list", "Lists all grids you own at least 50% of. Will give you positions if the server admin enables the option.")]
    [Permission(MyPromoteLevel.None)]
    public void List()
    {
        if (Context.Player is null)
        {
            Context.Respond("This is an in-game command");
            return;           
        }
            
        StringBuilder sb = new ();

        foreach (var entity in MyEntities.GetEntities())
        {
            var grid = entity as MyCubeGrid;
            if (grid == null || grid.Projector != null)
                continue;

            if (grid.BigOwners.Contains(Context.Player.IdentityId))
            {

                sb.AppendLine($"{grid.DisplayName} - {grid.GridSizeEnum} - {grid.BlocksCount} blocks - Position {(EssentialsPlugin.Instance.Config.UtilityShowPosition ? grid.PositionComp.GetPosition().ToString() : "Unknown")}");
                if (EssentialsPlugin.Instance.Config.MarkerShowPosition)
                {
                    var gridGPS = MyAPIGateway.Session?.GPS.Create(grid.DisplayName, ($"{grid.DisplayName} - {grid.GridSizeEnum} - {grid.BlocksCount} blocks"), grid.PositionComp.GetPosition(), true);

                    MyAPIGateway.Session?.GPS.AddGps(Context.Player.IdentityId, gridGPS);
                }
            }
        }

        ModCommunication.SendMessageTo(new DialogMessage("Grids List", $"Ships/Stations owned by {Context.Player.DisplayName}", sb.ToString()), Context.Player.SteamUserId);
    }

    private readonly string ExportPath = "ExportedGrids\\{0}.xml";

    [Command("export", "Export the given grid to the given file name.")]
    [Permission(MyPromoteLevel.Admin)]
    public void Export(string gridName, string exportName)
    {
        Directory.CreateDirectory("ExportedGrids");
        if (!Utilities.TryGetEntityByNameOrId(gridName, out var ent) || !(ent is IMyCubeGrid))
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
    [Permission(MyPromoteLevel.Admin)]
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

            targetName = Context.Player.Controller.ControlledEntity.Entity.DisplayName;
        }

        if (!Utilities.TryGetEntityByNameOrId(targetName, out IMyEntity? ent) || ent is null)
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
            var pos = MyEntities.FindFreePlaceCustom(ent.GetPosition(), grid.CalculateBoundingSphere().Radius);
                
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