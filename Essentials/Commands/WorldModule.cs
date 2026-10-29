using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using NLog;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems.BankingAndCurrency;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Game.World;
using Sandbox.Game.World.Generator;
using Sandbox.ModAPI;
using Torch.Commands;
using Torch.Mod;
using Torch.Mod.Messages;
using Torch.Commands.Permissions;
using Torch.Managers;
using Torch.Utils;
using VRage.Game;
using VRage.Game.ModAPI;

namespace Essentials.Commands;

public class WorldModule : CommandModule
{
    private static Logger _log = LogManager.GetCurrentClassLogger();

    [Command("identity clean", "Resets identities that have not logged in for X days to a brand-new state (grids, faction, GPS, reputation, credits). The identities are kept. Usage: identity clean <days> [includeNpcs]")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void CleanIdentities(int days, bool includeNpcs = false)
    {
        var identities = GetInactiveIdentities(days, includeNpcs);
        if (identities.Count == 0)
        {
            Context.Respond($"No identities found that have been inactive for more than {days} days.");
            return;
        }

        int grids = identities.Sum(identity => ResetIdentity(identity, removeIdentity: false));
        RemoveEmptyFactions();
        Context.Respond($"Reset {identities.Count} identities and deleted {grids} grids they owned.");
    }

    [Command("identity purge", "Removes identities that have not logged in for X days, together with everything they own (grids, faction membership, GPS, reputation, credits). Usage: identity purge <days> [includeNpcs]")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void PurgeIdentities(int days, bool includeNpcs = false)
    {
        var identities = GetInactiveIdentities(days, includeNpcs);
        if (identities.Count == 0)
        {
            Context.Respond($"No identities found that have been inactive for more than {days} days.");
            return;
        }

        int grids = identities.Sum(identity => ResetIdentity(identity, removeIdentity: true));
        RemoveEmptyFactions();
        Context.Respond($"Removed {identities.Count} identities and {grids} grids they owned.");
    }

    [Command("identity clear", "Resets one player's identity to a brand-new state (grids, faction, GPS, reputation, credits). The identity is kept.")]
    [Permission(MyPromoteLevel.Admin)]
    public void ClearIdentity(string playername)
    {
        var found = Utilities.GetIdentityByNameOrIds(playername);
        var identity = found == null ? null : MySession.Static.Players.TryGetIdentity(found.IdentityId);

        if (identity == null)
        {
            Context.Respond($"No Identity found for {playername}.  Try Again");
            return;
        }

        int grids = ResetIdentity(identity, removeIdentity: false);
        RemoveEmptyFactions();
        Context.Respond($"Reset identity {identity.DisplayName} and deleted {grids} grids they owned.");
    }

    /// <summary>
    /// Offline identities whose last login is older than the given number of days.
    /// NPC identities are only included when asked for.
    /// </summary>
    private static List<MyIdentity> GetInactiveIdentities(int days, bool includeNpcs)
    {
        var cutoff = DateTime.Now - TimeSpan.FromDays(days);
        return MySession.Static.Players.GetAllIdentities()
            .Where(identity => identity.LastLoginTime < cutoff)
            .Where(identity => !MySession.Static.Players.IsPlayerOnline(identity.IdentityId))
            .Where(identity => includeNpcs || !Utils.Ownership.IsNpcIdentity(identity.IdentityId))
            .ToList();
    }

    /// <summary>
    /// Returns an identity to a brand-new state: deletes grid groups it is the majority owner of (other owners'
    /// shared groups get its blocks instead), deletes its offline character, removes it from its faction and
    /// clears its GPS, reputation and credits. When removeIdentity is set the identity itself is removed as well.
    /// Returns the number of grids deleted.
    /// </summary>
    private static int ResetIdentity(MyIdentity identity, bool removeIdentity)
    {
        long id = identity.IdentityId;
        int deleted = 0;

        foreach (var group in GridFinder.GetAllGrids().Values)
        {
            if (!Utils.Ownership.GetAllOwnerIds(group).Contains(id))
                continue;

            long majority = Utils.Ownership.GetOwner(group);
            if (majority == id)
            {
                foreach (var grid in group)
                {
                    Utilities.EjectPilots(grid);
                    grid.Close();
                    deleted++;
                }
                continue;
            }

            // Shared group owned mostly by someone else: hand this identity's blocks to the majority owner.
            foreach (var grid in group)
            {
                grid.TransferBlocksBuiltByID(id, majority);
                foreach (var block in grid.GetFatBlocks().Where(b => b.OwnerId == id).ToList())
                {
                    // Ownership can only move to or from nobody, so go through 0.
                    block.ChangeBlockOwnerRequest(0, MyOwnershipShareModeEnum.Faction);
                    block.ChangeBlockOwnerRequest(majority, MyOwnershipShareModeEnum.Faction);
                }
            }
        }

        if (!MySession.Static.Players.IsPlayerOnline(id) && identity.Character is { MarkedForClose: false } character)
            character.Close();

        if (MySession.Static.Factions.GetPlayerFaction(id) != null)
            MyVisualScriptLogicProvider.KickPlayerFromFaction(id);

        var gpsApi = MyAPIGateway.Session.GPS;
        foreach (var gps in gpsApi.GetGpsList(id).ToList())
            gpsApi.RemoveGps(id, gps);

        var relations = _playerRelationsGet?.Invoke(MySession.Static.Factions);
        if (relations != null)
            foreach (var pair in relations.Keys.Where(pair => pair.RelateeId1 == id).ToList())
                relations.Remove(pair);

        long balance = MyBankingSystem.GetBalance(id);
        if (balance > 0)
            MyBankingSystem.ChangeBalance(id, -balance);

        if (removeIdentity)
        {
            // Passing the player id also removes the steam id mapping, so a returning player gets a fresh identity.
            MySession.Static.Players.TryGetPlayerId(id, out MyPlayer.PlayerId playerId);
            MySession.Static.Players.RemoveIdentity(id, playerId);
        }

        return deleted;
    }

    [Command("rep wipe", "Resets the reputation on the server")]
    public void WipeReputation(bool removePlayerToFaction = true, bool removeFactionToFaction = true)
    {
        var count = WipeRep(removePlayerToFaction, removeFactionToFaction);
        Context.Respond($"Wiped {count} reputations");
    }
        
    [Command("faction clean", "Removes factions with fewer than the given number of players.")]
    public void CleanFactions(int memberCount = 1)
    {
        int count = CleanFaction_Internal(memberCount);

        Context.Respond($"Removed {count} factions with fewer than {memberCount} members.");
    }

    [Command("faction remove", "removes faction by tag name")]
    [Permission(MyPromoteLevel.Admin)]
    public void RemoveFaction(string? tag)
    {
        if (tag == null)
        {
            Context.Respond("You need to add a faction tag to remove");
            return;
        }

        var fac = MySession.Static.Factions.TryGetFactionByTag(tag);
        if (fac == null || !MySession.Static.Factions.FactionTagExists(tag))
        {
            Context.Respond($"{tag} is not a faction on this server");
            return;
        }

        // Kick from a snapshot, since kicking changes the member list. The founder goes last so the faction
        // always has a founder while other members remain.
        var members = fac.Members.Keys.ToList();
        var kickOrder = members.Where(m => m != fac.FounderId).ToList();
        if (members.Contains(fac.FounderId))
            kickOrder.Add(fac.FounderId);

        foreach (var member in kickOrder)
        {
            if (MySession.Static.Factions.TryGetFactionById(fac.FactionId) == null)
                break;
            MyFactionCollection.KickMember(fac.FactionId, member);
        }

        if (MySession.Static.Factions.TryGetFactionById(fac.FactionId) != null)
            RemoveFaction(fac);
        Context.Respond(MySession.Static.Factions.FactionTagExists(tag)
            ? $"{tag} removal failed"
            : $"{tag} removal successful");
    }

    [Command("faction info", "Lists the members of every faction.")]
    [Permission(MyPromoteLevel.Admin)]
    public void FactionInfo()
    {
        StringBuilder sb = new StringBuilder();

        foreach (KeyValuePair<long, MyFaction> factionId in MySession.Static.Factions)
        {
            var faction = factionId.Value;
            double memberCount = faction.Members.Count();
            sb.AppendLine();
            if (faction.IsEveryoneNpc())
            {
                sb.AppendLine($"{faction.Tag} - {memberCount} NPC found in this faction");
                continue;
            }
            sb.AppendLine($"{faction.Tag} - {memberCount} players in this faction");
            foreach (var member in faction.Members.Keys.ToList())
            {
                // Members whose identity was deleted have no name to show.
                var name = MySession.Static.Players.TryGetIdentity(member)?.DisplayName;
                if (string.IsNullOrEmpty(name))
                    continue;
                sb.AppendLine(name);
            }
        }
        if (Context.Player == null)
            Context.Respond(sb.ToString());
        else if (Context?.Player?.SteamUserId > 0)
        {
            ModCommunication.SendMessageTo(new DialogMessage("Faction Info", null, sb.ToString()), Context.Player.SteamUserId);
        }
    }

    private static void RemoveEmptyFactions()
    {
        CleanFaction_Internal(1);
    }

    private static int CleanFaction_Internal(int memberCount = 1)
    {
        int result = 0;

        foreach (var faction in MySession.Static.Factions.ToList())
        {
            if ((faction.Value.IsEveryoneNpc() || !faction.Value.AcceptHumans) && faction.Value.Members.Count != 0) //needed to add this to catch the 0 member factions
                continue;

            int validmembers = 0;
            //O(2n)
            foreach (var member in faction.Value.Members)
            {
                // Members whose identity no longer exists do not count.
                if (!MySession.Static.Players.HasIdentity(member.Key))
                    continue;

                validmembers++;

                if (validmembers >= memberCount)
                    break;
            }

            if (validmembers >= memberCount)
                continue;

            RemoveFaction(faction.Value);
            result++;
        }

        return result;
    }

    private static bool RemoveFromFaction_Internal(MyIdentity identity)
    {
        var fac = MySession.Static.Factions.GetPlayerFaction(identity.IdentityId);
        if (fac == null)
            return false;
 
        /*
         * VisualScriptLogicProvider takes care of removal of faction if last
         * identity is kicked, and promotes the next player in line to Founder
         * if the founder is being kicked.
         *
         * Factions must have a founder otherwise calls like MyFaction.Members.Keys will NRE.
         */
        MyVisualScriptLogicProvider.KickPlayerFromFaction(identity.IdentityId);

        return true;
    }
        
    private static MethodInfo? _factionChangeSuccessInfo = typeof(MyFactionCollection).GetMethod("FactionStateChangeSuccess", BindingFlags.NonPublic | BindingFlags.Static);
        
    //TODO: This should probably be moved into Torch base, but I honestly cannot be bothered <<-- Torch is fucked, don't touch torch.
    /// <summary>
    /// Removes a faction from the server and all clients because Keen fucked up their own system.
    /// </summary>
    /// <param name="faction"></param>
    private static void RemoveFaction(MyFaction faction)
    {
        //bypass the check that says the server doesn't have permission to delete factions
        //_applyFactionState(MySession.Static.Factions, MyFactionStateChange.RemoveFaction, faction.FactionId, faction.FactionId, 0L, 0L);
        //MyMultiplayer.RaiseStaticEvent(s =>
        //        (Action<MyFactionStateChange, long, long, long, long>) Delegate.CreateDelegate(typeof(Action<MyFactionStateChange, long, long, long, long>), _factionStateChangeReq),
        //    MyFactionStateChange.RemoveFaction, faction.FactionId, faction.FactionId, faction.FounderId, faction.FounderId);
        NetworkManager.RaiseStaticEvent(_factionChangeSuccessInfo, MyFactionStateChange.RemoveFaction, faction.FactionId, faction.FactionId, 0L, 0L);
        if(!MyAPIGateway.Session.Factions.FactionTagExists(faction.Tag)) return;
        MyAPIGateway.Session.Factions.RemoveFaction(faction.FactionId); //Added to remove factions that got through the crack
    }

    private static int FixBlockOwnership()
    {
        int count = 0;
        foreach (var entity in MyEntities.GetEntities())
        {
            var grid = entity as MyCubeGrid;
            if (grid == null)
                continue;
            var owner = Utils.Ownership.GetOwner(grid);
            var share = owner == 0 ? MyOwnershipShareModeEnum.All : MyOwnershipShareModeEnum.Faction;
            foreach (var block in grid.GetFatBlocks())
            {
                if (block.OwnerId == 0 || MySession.Static.Players.HasIdentity(block.OwnerId))
                    continue;

                block.ChangeOwner(owner, share);
                count++;
            }
        }
        return count;
    }

    private static readonly FieldInfo? GpsDicField = typeof(MyGpsCollection).GetField("m_playerGpss", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? SeedParamField = typeof(MyProceduralWorldGenerator).GetField("m_existingObjectsSeeds", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly FieldInfo? CamerasField = typeof(MySession).GetField("Cameras", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? AllCamerasField = CamerasField?.FieldType.GetField("m_entityCameraSettings", BindingFlags.NonPublic | BindingFlags.Instance);

    [Command("sandbox clean", "Cleans up junk data from the sandbox file")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void CleanSandbox()
    {
        int count = 0;
        var validIdentities = new HashSet<long>();
        var idCache = new HashSet<long>();

        //find all identities owning a block
        foreach (var entity in MyEntities.GetEntities())
        {
            var grid = entity as MyCubeGrid;
            if (grid == null)
                continue;
            validIdentities.UnionWith(Utils.Ownership.GetAllOwnerIds(grid));
        }

        foreach (var online in MySession.Static.Players.GetOnlinePlayers())
        {
            validIdentities.Add(online.Identity.IdentityId);
        }

        //might not be necessary, but just in case
        validIdentities.Remove(0);

        //clean identities that don't own any blocks, or don't have a steam ID for whatever reason
        foreach (var identity in MySession.Static.Players.GetAllIdentities().ToList())
        {
            if (Utils.Ownership.IsNpcIdentity(identity.IdentityId)  || string.IsNullOrEmpty(identity.DisplayName))
            {
                validIdentities.Add(identity.IdentityId);
                continue;
            }

            if (validIdentities.Contains(identity.IdentityId))
            {
                continue;
            }

            RemoveFromFaction_Internal(identity);
            MySession.Static.Players.TryGetPlayerId(identity.IdentityId, out MyPlayer.PlayerId playerId);
            MySession.Static.Players.RemoveIdentity(identity.IdentityId, playerId);
            validIdentities.Remove(identity.IdentityId);
            count++;
        }

        //reset ownership of blocks belonging to deleted identities
        count += FixBlockOwnership();

        //clean up empty factions
        count += CleanFaction_Internal();

        //cleanup reputations
        count += CleanupReputations();

        //Keen, for the love of god why is everything about GPS internal.
        var playerGpss = GpsDicField?.GetValue(MySession.Static.Gpss) as Dictionary<long, Dictionary<int, MyGps>>;
        if (playerGpss is not null)
        {
            foreach (var id in playerGpss.Keys)
            {
                if (!validIdentities.Contains(id))
                    idCache.Add(id);
            }

            foreach (var id in idCache)
                playerGpss.Remove(id);
        }

        count += idCache.Count;
        idCache.Clear();

        var g = MySession.Static.GetComponent<MyProceduralWorldGenerator>();
        var f = SeedParamField?.GetValue(g) as HashSet<MyObjectSeedParams>;
        count += f?.Count ?? 0;
        f?.Clear();
            
        //TODO
        /*
        foreach (var history in MySession.Static.ChatHistory)
        {
            if (!validIdentities.Contains(history.Key))
                idCache.Add(history.Key);
        }

        foreach (var id in idCache)
        {
            MySession.Static.ChatHistory.Remove(id);
        }
        count += idCache.Count;
        idCache.Clear();

        //delete chat history for deleted factions
        for (int i = MySession.Static.FactionChatHistory.Count - 1; i >= 0; i--)
        {
            var history = MySession.Static.FactionChatHistory[i];
            if (MySession.Static.Factions.TryGetFactionById(history.FactionId1) == null || MySession.Static.Factions.TryGetFactionById(history.FactionId2) == null)
            {
                count++;
                MySession.Static.FactionChatHistory.RemoveAtFast(i);
            }
        }
        */
            
        var cf = AllCamerasField?.GetValue(CamerasField?.GetValue(MySession.Static)) as Dictionary<MyPlayer.PlayerId, Dictionary<long, MyEntityCameraSettings>>;
        count += cf?.Count ?? 0;
        cf?.Clear();

        Context.Respond($"Removed {count} unnecessary elements.");
    }

    [ReflectedGetter(Name = "m_relationsBetweenFactions", Type = typeof(MyFactionCollection))]
    private static Func<MyFactionCollection, Dictionary<MyFactionCollection.MyRelatablePair, Tuple<MyRelationsBetweenFactions, int>>>? _relationsGet = null;
        
    [ReflectedGetter(Name = "m_relationsBetweenPlayersAndFactions", Type = typeof(MyFactionCollection))]
    private static Func<MyFactionCollection, Dictionary<MyFactionCollection.MyRelatablePair, Tuple<MyRelationsBetweenFactions, int>>>? _playerRelationsGet = null;

    private static int WipeRep(bool removePlayerToFaction, bool removeFactionToFaction)
    {
        var result = 0;
        var collection0 = _relationsGet?.Invoke(MySession.Static.Factions);
        var collection1 = _playerRelationsGet?.Invoke(MySession.Static.Factions);

        if (removeFactionToFaction)
        {
            if (collection0 != null)
                foreach (var pair in collection0.Keys.ToList())
                {
                    collection0.Remove(pair);
                    result++;
                }
        }

        if (collection1 != null)
            if (removePlayerToFaction)
            {
                foreach (var pair in collection1.Keys.ToList())
                {
                    collection1.Remove(pair);
                    result++;
                }
            }

        return result;

    }
    private static int CleanupReputations()
    {
        var collection = _relationsGet?.Invoke(MySession.Static.Factions);
        var collection2 = _playerRelationsGet?.Invoke(MySession.Static.Factions);


        var validIdentities = new HashSet<long>();

        //find all identities owning a block
        foreach (var entity in MyEntities.GetEntities())
        {
            var grid = entity as MyCubeGrid;
            if (grid == null)
                continue;
            validIdentities.UnionWith(Utils.Ownership.GetAllOwnerIds(grid));
        }

        //find online identities
        foreach (var online in MySession.Static.Players.GetOnlinePlayers())
        {
            validIdentities.Add(online.Identity.IdentityId);
        }

        foreach (var identity in MySession.Static.Players.GetAllIdentities().ToList())
        {
            if (Utils.Ownership.IsNpcIdentity(identity.IdentityId))
            {
                validIdentities.Add(identity.IdentityId);
            }
        }

        //Add Factions with at least one member to valid identities
        foreach (var faction in MySession.Static.Factions.Factions.Where(x=>x.Value.Members.Count > 0))
        {
            validIdentities.Add(faction.Key);
        }

        //might not be necessary, but just in case
        validIdentities.Remove(0);
        var result = 0;

        var collection0List = collection?.Keys.ToList();
        var collection1List = collection2?.Keys.ToList();

        if (collection0List != null)
            foreach (var pair in collection0List)
            {
                if (validIdentities.Contains(pair.RelateeId1) && validIdentities.Contains(pair.RelateeId2))
                    continue;
                collection?.Remove(pair);
                result++;
            }

        if (collection1List != null)
            foreach (var pair in collection1List)
            {
                if (validIdentities.Contains(pair.RelateeId1) && validIdentities.Contains(pair.RelateeId2))
                    continue;
                collection2?.Remove(pair);
                result++;
            }

        //_relationsSet.Invoke(MySession.Static.Factions,collection);
        //_playerRelationsSet.Invoke(MySession.Static.Factions,collection2);
        return result;
    }
}