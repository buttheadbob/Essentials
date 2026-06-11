using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using VRage.Game;

namespace Essentials.Utils;

public class Ownership
{
    public static long GetOwner(MyCubeGrid grid)
    {
        Dictionary<long, int> ownerCounts = new();

        foreach (var block in grid.GetFatBlocks())
        {
            if (block.IDModule is null) continue;
            long ownerId = block.IDModule.Owner;
            if (ownerId == 0) continue;

            ownerCounts.TryGetValue(ownerId, out int count);
            ownerCounts[ownerId] = count + 1;
        }

        if (ownerCounts.Count == 0) return 0L;

        long bestOwner = 0;
        int bestCount = 0;
        foreach (var kvp in ownerCounts)
        {
            if (kvp.Value > bestCount)
            {
                bestCount = kvp.Value;
                bestOwner = kvp.Key;
            }
        }
        return bestOwner;
    }

    public static HashSet<long> GetAllOwnerIds(MyCubeGrid grid)
    {
        HashSet<long> owners = new();

        foreach (var block in grid.GetFatBlocks())
        {
            if (block.IDModule is null) continue;
            long ownerId = block.IDModule.Owner;
            if (ownerId != 0)
                owners.Add(ownerId);
        }

        return owners;
    }

    public static OwnerType GetOwnerType(MyCubeGrid grid)
    {
        var ownerId = GetOwner(grid);
        if (ownerId == 0L)
            return OwnerType.Nobody;

        return IsNpcIdentity(ownerId) ? OwnerType.Npc : OwnerType.Player;
    }

    public static bool IsNpcIdentity(long identityId)
    {
        var faction = MySession.Static.Factions.TryGetPlayerFaction(identityId);
        if (faction != null)
            return faction.FactionType != MyFactionTypes.PlayerMade && faction.FactionType != MyFactionTypes.None;

        return MySession.Static.Players.TryGetSteamId(identityId) == 0;
    }

    public enum OwnerType
    {
        Nobody,
        Npc,
        Player
    }
}
