using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game.Entities;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using Torch.Commands;
using Torch.Commands.Permissions;
using Torch.Utils;
using Torch.API.Managers;
using VRage.Collections;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Game;
using VRage.Network;
using VRage.Replication;
using Sandbox.Game;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.EntityComponents;
using VRage.Game.ModAPI.Interfaces;
// ReSharper disable InconsistentNaming

namespace Essentials;

[Category("entities")]
public class EntityModule : CommandModule
{
#pragma warning disable 649
    [ReflectedGetter(Name = "m_clientStates")]
    private static Func<MyReplicationServer, IDictionary> _clientStates = null!;

    private const string CLIENT_DATA_TYPE_NAME = "VRage.Network.MyClient, VRage";
    [ReflectedGetter(TypeName = CLIENT_DATA_TYPE_NAME, Name = "Replicables")]
    private static Func<object, MyConcurrentDictionary<IMyReplicable, MyReplicableClientData>>? _replicables;

    [ReflectedMethod(Name = "RemoveForClient", OverrideTypeNames = [null, CLIENT_DATA_TYPE_NAME, null])]
    private static Action<MyReplicationServer, IMyReplicable, object, bool>? _removeForClient;

    [ReflectedMethod(Name = "ForceReplicable")]
    private static Action<MyReplicationServer, IMyReplicable, Endpoint>? _forceReplicable;
#pragma warning restore 649

    private static Dictionary<ulong, DateTime> _commandtimeout = [];

    [Command("refresh", "Resyncs all entities for the player running the command.")]
    [Permission(MyPromoteLevel.None)]
    public void Refresh()
    {
        if (Context.Player == null)
            return;

        var steamid = Context.Player.SteamUserId;
        if (_commandtimeout.TryGetValue(steamid, out DateTime lastcommand))
        {
            TimeSpan remaining = TimeSpan.FromMinutes(1) - (DateTime.Now - lastcommand);
            if (remaining > TimeSpan.Zero)
            {
                Context.Respond($"Cooldown active. You can use this command again in {Math.Ceiling(remaining.TotalSeconds):N0} seconds");
                return;
            }
                
            _commandtimeout[steamid] = DateTime.Now;
        }
        else
        {
            _commandtimeout.Add(steamid, DateTime.Now);
        }

        var playerEndpoint = new Endpoint(Context.Player.SteamUserId, 0);
        var replicationServer = (MyReplicationServer)MyMultiplayer.ReplicationLayer;
        var clientDataDict = _clientStates.Invoke(replicationServer);
        object clientData;
        try
        {
            clientData = clientDataDict[playerEndpoint];
        }
        catch
        {
            return;
        }

        var clientReplicables = _replicables!.Invoke(clientData);

        var replicableList = new List<IMyReplicable>(clientReplicables.Count);
        foreach (var pair in clientReplicables)
            replicableList.Add(pair.Key);

        foreach (var replicable in replicableList)
        {
            _removeForClient?.Invoke(replicationServer, replicable, clientData, true);
            _forceReplicable?.Invoke(replicationServer, replicable, playerEndpoint);
        }

        Context.Respond($"Forced replication of {replicableList.Count} entities.");
    }

    [Command("stop", "Stops an entity from moving")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void Stop(string entityName)
    {
        if (!Utilities.TryGetEntityByNameOrId(entityName, out IMyEntity? entity))
        {
            Context.Respond($"Entity '{entityName}' not found.");
            return;
        }

        entity!.Physics?.ClearSpeed();
        Context.Respond($"Entity '{entity.DisplayName}' stopped");
    }

    [Command("delete", "Delete an entity.")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void Delete(string entityName)
    {
        var name = entityName;
        if (string.IsNullOrEmpty(name))
            return;

        if (!Utilities.TryGetEntityByNameOrId(name, out IMyEntity? entity))
        {
            Context.Respond($"Entity '{name}' not found.");
            return;
        }

        if (entity is IMyCharacter)
        {
            Context.Respond("You cannot delete characters.");
            return;
        }

        if (entity is MyCubeGrid grid)
            Utilities.EjectPilots(grid);

        entity!.Close();
        Context.Respond($"Entity '{entity.DisplayName}' deleted");
    }

    [Command("kill", "kill a player.")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void Kill(string playerName)
    {
        /*
         * First we try killing the player when hes online. This is easy and fast
         * and can also kill the player while being seated.
         */
        var player = Utilities.GetPlayerByNameOrId(playerName);
        if (player != null) 
        {
            MyVisualScriptLogicProvider.SetPlayersHealth(player.IdentityId, 0);

            Context.Torch.CurrentSession?.Managers?.GetManager<IChatManagerServer>()?.SendMessageAsSelf
                ($"{player.DisplayName} was killed by an admin [{Context.Player?.DisplayName ?? "Console Command"}]");

            return;
        }

        /*
         * If we could not find the player there is a chance he is offline, in that case we try inflicting
         * damage to the character as the VST will not help us with offline characters.
         */
        if (!Utilities.TryGetEntityByNameOrId(playerName, out IMyEntity? entity)) {
            Context.Respond($"Entity '{playerName}' not found.");
            return;
        }

        if (entity is IMyCharacter) 
        {
            var destroyable = entity as IMyDestroyableObject;

            destroyable?.DoDamage(1000f, MyDamageType.Radioactivity, true);

            Context.Torch.CurrentSession?.Managers?.GetManager<IChatManagerServer>()?.SendMessageAsSelf
                ($"{entity.DisplayName} was killed by an admin");
        }
    }

    [Command("find", "Find entities with the given text in their name.")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void Find(string name)
    {
        if (string.IsNullOrEmpty(name))
            return;

        var sb = new StringBuilder("Found entities:\n");
        foreach (var entity in MyEntities.GetEntities())
        {
            if (entity is IMyVoxelBase voxel && (voxel.StorageName?.Contains(name, StringComparison.CurrentCultureIgnoreCase) ?? false))
                sb.AppendLine($"{voxel.StorageName} ({entity.EntityId})");
            else if (entity?.DisplayName?.Contains(name, StringComparison.CurrentCultureIgnoreCase) ?? false)
                //This can be null??? :keen:
                sb.AppendLine($"{entity.DisplayName} ({entity.EntityId})");
        }

        Context.Respond(sb.ToString());
    }

    [Command("poweroff", "Turns off every power-producing block (reactors, batteries, solar, wind, engines, ...) on a grid and everything attached by rotors, hinges and pistons. Usage: poweroff <grid> [includeConnectorDocked]")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void PowerOff(string name, bool includeConnectorDocked = false)
    {
        SetPower(name, false, includeConnectorDocked);
    }

    [Command("poweron", "Turns on every power-producing block (reactors, batteries, solar, wind, engines, ...) on a grid and everything attached by rotors, hinges and pistons. Usage: poweron <grid> [includeConnectorDocked]")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void PowerOn(string name, bool includeConnectorDocked = false)
    {
        SetPower(name, true, includeConnectorDocked);
    }

    private void SetPower(string name, bool enabled, bool includeConnectorDocked)
    {
        if (string.IsNullOrEmpty(name))
            return;

        if (!Utilities.TryGetEntityByNameOrId(name, out IMyEntity? entity))
        {
            Context.Respond($"Entity '{name}' not found.");
            return;
        }

        if (entity is not MyCubeGrid grid)
        {
            Context.Respond($"'{entity!.DisplayName}' is not a grid.");
            return;
        }

        var grids = GridFinder.GetGroup(grid, includeConnectorDocked ? GridLinkTypeEnum.Physical : GridLinkTypeEnum.Mechanical);
        int changed = 0;
        foreach (var block in grids.SelectMany(g => g.GetFatBlocks<MyFunctionalBlock>()))
        {
            // Any block that produces electricity, so modded and newer power blocks are included.
            var source = block.Components.Get<MyResourceSourceComponent>();
            if (source == null || !source.ResourceTypes.Contains(MyResourceDistributorComponent.ElectricityId))
                continue;

            if (block.Enabled == enabled)
                continue;

            block.Enabled = enabled;
            changed++;
        }

        Context.Respond($"Turned {(enabled ? "on" : "off")} {changed} power block(s) on {grids.Count} grid(s) of '{grid.DisplayName}'.");
    }

    [Command("eject", "Ejects a specific player from any block they are seated in, or all players in the server if run with 'all'")]
    public void Eject(string playerName) 
    {

        if (playerName.ToLower() == "all") 
            EjectAllPlayers();
        else 
            EjectSinglePlayer(playerName);
    }

    private void EjectAllPlayers() 
    {

        int ejectedPlayersCount = 0;

        foreach (var grid in MyEntities.GetEntities().OfType<MyCubeGrid>().ToList()) 
        foreach (var controller in grid.GetFatBlocks<MyShipController>()) 
            if (controller.Pilot != null) 
            {
                controller.Use();
                ejectedPlayersCount++;
            }

        Context.Respond($"Ejected '{ejectedPlayersCount}' players from their seats.");
    }

    private void EjectSinglePlayer(string playerName) 
    {

        /* We check first if the player is among the online players before looping over all grids for nothing. */
        var player = Utilities.GetPlayerByNameOrId(playerName);
        if (player != null) 
        {
            /* If he is online we check if he is currently seated. If he is eject him. */
            if (player?.Controller.ControlledEntity is MyCockpit controller) 
            {
                controller.Use();
                Context.Respond($"Player '{playerName}' ejected.");
            } 
            else 
                Context.Respond("Player not seated.");

            return;
        }

        foreach (var grid in MyEntities.GetEntities().OfType<MyCubeGrid>().ToList()) 
        {
            foreach (var controller in grid.GetFatBlocks<MyShipController>()) 
            {
                var pilot = controller.Pilot;

                if (pilot != null && pilot.DisplayName == playerName) 
                {
                    controller.Use();

                    Context.Respond($"Player '{playerName}' ejected.");

                    /* We found our player. so no need to continue looking */
                    return;
                }
            }
        }

        Context.Respond("Offline player not found or seated.");
    }
}