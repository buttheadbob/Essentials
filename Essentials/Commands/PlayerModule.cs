using System;
using System.Linq;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Torch.Commands;
using Torch.Commands.Permissions;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using Torch.API.Managers;
using VRageMath;

namespace Essentials;

public class PlayerModule : CommandModule
{
    private EssentialsPlugin Plugin => (EssentialsPlugin)Context.Plugin;

    [Command("say", "Say a message as the server.")]
    public void Say(string message)
    {
        Context.Torch.CurrentSession?.Managers?.GetManager<IChatManagerServer>()?.SendMessageAsSelf(Context.RawArgs);
    }

    [Command("tp", "Teleport one entity to another. Grids move together with everything attached by rotors, hinges and pistons. Usage: tp <entity> <destination> [includeConnectorDocked]")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void Teleport(string? entityToMove, string? destination, bool includeConnectorDocked = false)
    {
        var destEntity = string.IsNullOrEmpty(destination) ? GetSelf() : ResolveEntity(destination!);
        if (destEntity == null)
        {
            if (!string.IsNullOrEmpty(destination))
                Context.Respond("Destination entity not found");
            return;
        }

        var targetEntity = string.IsNullOrEmpty(entityToMove) ? GetSelf() : ResolveEntity(entityToMove!);
        if (targetEntity == null)
        {
            if (!string.IsNullOrEmpty(entityToMove))
                Context.Respond("Target entity not found.");
            return;
        }

        if (targetEntity is MyCubeGrid grid)
        {
            TeleportGrid(grid, destEntity, includeConnectorDocked);
            return;
        }

        var targetPos = MyEntities.FindFreePlaceCustom(destEntity.GetPosition(), (float)targetEntity.WorldAABB.Extents.Max());
        if (targetPos == null)
        {
            Context.Respond("No free place to teleport.");
            return;
        }

        var matrix = targetEntity.WorldMatrix;
        matrix.Translation = targetPos.Value;
        targetEntity.Teleport(matrix);
        Context.Respond($"Teleported {targetEntity.DisplayName}.");
    }

    [Command("tpto", "Teleport directly to an another entity. Usage: tpto <destination> [entity] [includeConnectorDocked]")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void TeleportTo(string destination, string? entityToMove = null, bool includeConnectorDocked = false)
    {
        Teleport(entityToMove, destination, includeConnectorDocked);
    }

    [Command("tphere", "Teleport another entity directly to you. Usage: tphere <entity> [destination] [includeConnectorDocked]")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void TeleportHere(string entityToMove, string? destination = null, bool includeConnectorDocked = false)
    {
        Teleport(entityToMove, destination, includeConnectorDocked);
    }

    /// <summary>
    /// Moves a grid with its mechanical group. Seated characters move with it, so pilots stay seated.
    /// Grids docked by connectors are only moved when asked for, since SE teleports the whole physical group.
    /// </summary>
    private void TeleportGrid(MyCubeGrid grid, IMyEntity destEntity, bool includeConnectorDocked)
    {
        var mechanical = GridFinder.GetGroup(grid);
        var physical = GridFinder.GetGroup(grid, GridLinkTypeEnum.Physical);

        if (!includeConnectorDocked && physical.Count > mechanical.Count)
        {
            Context.Respond($"{grid.DisplayName} is docked to other grids by connectors. Undock it first, or add 'true' to move everything docked to it.");
            return;
        }

        var moving = includeConnectorDocked ? physical : mechanical;
        var destRoot = destEntity is MyCubeBlock block ? block.CubeGrid : destEntity;
        if (moving.Any(g => g == destRoot))
        {
            Context.Respond("The destination is part of the grids being moved.");
            return;
        }

        var bounds = BoundingBoxD.CreateInvalid();
        foreach (var g in moving)
            bounds.Include(g.PositionComp.WorldAABB);

        var targetPos = MyEntities.FindFreePlaceCustom(destEntity.GetPosition(), (float)bounds.HalfExtents.Max());
        if (targetPos == null)
        {
            Context.Respond("No free place to teleport.");
            return;
        }

        var matrix = grid.WorldMatrix;
        matrix.Translation += targetPos.Value - bounds.Center;
        grid.Teleport(matrix);
        Context.Respond($"Teleported {grid.DisplayName} ({moving.Count} grid(s)).");
    }

    /// <summary>
    /// The calling player's controlled entity (their ship when seated). Responds and returns null when called
    /// from the console or while dead.
    /// </summary>
    private IMyEntity? GetSelf()
    {
        if (Context.Player == null)
        {
            Context.Respond("Both the entity and the destination must be given when using the console.");
            return null;
        }

        var entity = Context.Player.Controller?.ControlledEntity?.Entity;
        if (entity == null || Context.Player.Character == null || Context.Player.Character.IsDead)
        {
            Context.Respond("You must be alive in the world to use this command.");
            return null;
        }

        return entity is MyCubeBlock block ? block.CubeGrid : entity;
    }

    /// <summary>
    /// Finds an online player by name (their ship when seated) or any entity by name or id.
    /// </summary>
    private static IMyEntity? ResolveEntity(string nameOrId)
    {
        var playerEntity = Utilities.GetPlayerByNameOrId(nameOrId)?.Controller?.ControlledEntity?.Entity;
        if (playerEntity != null)
            return playerEntity is MyCubeBlock block ? block.CubeGrid : playerEntity;

        Utilities.TryGetEntityByNameOrId(nameOrId, out var entity);
        return entity is MyCubeBlock cubeBlock ? cubeBlock.CubeGrid : entity;
    }

    [Command("w", "Send a private message to another player.")]
    [Permission(MyPromoteLevel.None)]
    public void Whisper(string playerName)
    {
        if (Context.Args.Count < 2)
        {
            Context.Respond("Usage: !w <player> <message>");
            return;
        }

        // Skip the player name in the raw text, which may be quoted.
        var raw = Context.RawArgs.TrimStart();
        int nameEnd = raw.StartsWith("\"")
            ? raw.IndexOf('"', 1)
            : raw.IndexOf(' ');
        if (nameEnd < 0 || nameEnd + 1 >= raw.Length)
        {
            Context.Respond("Usage: !w <player> <message>");
            return;
        }

        var message = raw.Substring(nameEnd + 1).Trim();
        var player = Context.Torch.CurrentSession?.Managers?.GetManager<IMultiplayerManagerBase>()?.GetPlayerByName(playerName);

        if (player == null)
        {
            Context.Respond($"Player '{playerName}' not found.");
            return;
        }

        Context.Torch.CurrentSession?.Managers?.GetManager<IChatManagerServer>()?.SendMessageAsOther(Context.Player?.DisplayName ?? "Server", message, Color.Red, player.SteamUserId, MyFontEnum.Red);
    }

    [Command("kick", "Kick a player from the game.")]
    [Permission(MyPromoteLevel.Moderator)]
    public void Kick(string playerName)
    {
        IMyPlayer? player = Utilities.GetPlayerByNameOrId(playerName);
        if (player != null)
        {
            Context.Torch.CurrentSession?.Managers?.GetManager<IMultiplayerManagerServer>()?.KickPlayer(player.SteamUserId);
            Context.Respond($"Player '{player.DisplayName}' kicked.");
        }
        else
        {
            Context.Respond("Player not found.");
        }
    }

    [Command("ban", "Ban a player from the game.")]
    [Permission(MyPromoteLevel.Moderator)]
    public void Ban(string nameOrSteamId)
    {
        var man = Context.Torch.CurrentSession.Managers.GetManager<IMultiplayerManagerServer>();
        var isId = ulong.TryParse(nameOrSteamId, out var steamId);

        foreach (var identity in MySession.Static.Players.GetAllIdentities())
        {
            var id = MySession.Static.Players.TryGetSteamId(identity.IdentityId);
            if (id != 0 && (identity.DisplayName == nameOrSteamId || id == steamId))
            {
                man.BanPlayer(id);
                Context.Respond($"Player {identity.DisplayName} banned. ({id})");
                return;
            }
        }

        if (isId)
        {
            man.BanPlayer(steamId);
            Context.Respond($"Steam ID {steamId} banned.");
            return;
        }
            
        Context.Respond($"Player '{nameOrSteamId}' not found.");
    }

    [Command("unban", "Unban a player from the game.")]
    [Permission(MyPromoteLevel.Moderator)]
    public void Unban(string nameOrSteamId)
    {
        var man = Context.Torch.CurrentSession.Managers.GetManager<IMultiplayerManagerServer>();
        var isId = ulong.TryParse(nameOrSteamId, out var steamId);

        foreach (var identity in MySession.Static.Players.GetAllIdentities())
        {
            var id = MySession.Static.Players.TryGetSteamId(identity.IdentityId);
            if (id != 0 && (identity.DisplayName == nameOrSteamId || id == steamId))
            {
                man.BanPlayer(id, false);
                Context.Respond($"Player {identity.DisplayName} unbanned. ({id})");
                return;
            }
        }

        if (isId)
        {
            man.BanPlayer(steamId, false);
            Context.Respond($"Steam ID {steamId} unbanned.");
            return;
        }
            
        Context.Respond($"Player '{nameOrSteamId}' not found.");
    }

    [Command("motd", "Show the server's Message of the Day.")]
    [Permission(MyPromoteLevel.None)]
    public void Motd()
    {
        if (Context.Player is not MyPlayer player)
        {
            Context.Respond("The MOTD can only be shown to a player in-game.");
            return;
        }

        Plugin.SendMotd(player, false);
    }
}