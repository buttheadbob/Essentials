using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.World;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Sandbox.ModAPI;
using VRageMath;
using Vector3D = VRageMath.Vector3D;

namespace Essentials.Commands
{
    /// <summary>
    /// Conditions are evaluated per mechanical group (a grid plus everything attached by rotors, hinges and pistons).
    /// Methods taking IReadOnlyList&lt;MyCubeGrid&gt; decide for the whole group. Methods taking a single MyCubeGrid
    /// match when any grid in the group matches; their inverted form matches when no grid in the group matches.
    /// </summary>
    [ConditionModule]
    public static class ConditionsImplementations
    {
        [Condition("name", helpText: "Finds grids whose main grid name matches. Accepts regex format.")]
        public static bool NameMatches(IReadOnlyList<MyCubeGrid> group, string str)
        {
            var name = GridFinder.GetMainGrid(group).DisplayName;
            if (string.IsNullOrEmpty(name))
                return false;

            return Regex.IsMatch(name, str);
        }

        [Condition("blockslessthan", helpText: "Finds grids with less than the given number of blocks (whole group).")]
        public static bool BlocksLessThan(IReadOnlyList<MyCubeGrid> group, int count)
        {
            return group.Sum(g => g.BlocksCount) < count;
        }

        [Condition("pcugreaterthan", helpText: "Finds grids with more than the given number of PCU (whole group).")]
        public static bool PCUGreaterThan(IReadOnlyList<MyCubeGrid> group, int pcu)
        {
            return group.Sum(g => g.BlocksPCU) > pcu;
        }

        [Condition("pculessthan", helpText: "Finds grids with less than the given number of PCU (whole group).")]
        public static bool PCULessThan(IReadOnlyList<MyCubeGrid> group, int pcu)
        {
            return group.Sum(g => g.BlocksPCU) < pcu;
        }

        [Condition("hasgridtype", helpText: "Finds grids whose main grid is the specified type (large | small | ship | static).")]
        public static bool HasGridType(IReadOnlyList<MyCubeGrid> group, string gridType)
        {
            if (string.IsNullOrEmpty(gridType))
                return false;

            var grid = GridFinder.GetMainGrid(group);

            if (string.Compare(gridType, "static", StringComparison.InvariantCultureIgnoreCase) == 0)
                return grid.IsStatic;

            if (string.Compare(gridType, "ship", StringComparison.InvariantCultureIgnoreCase) == 0)
                return !grid.IsStatic;

            if (string.Compare(gridType, "large", StringComparison.InvariantCultureIgnoreCase) == 0)
                return grid.GridSizeEnum == VRage.Game.MyCubeSize.Large;

            if (string.Compare(gridType, "small", StringComparison.InvariantCultureIgnoreCase) == 0)
                return grid.GridSizeEnum == VRage.Game.MyCubeSize.Small;

            // In all other cases, just return false.
            return false;
        }

        [Condition("hasownertype", helpText: "Finds grids whose majority owner is the specified type (npc | player | nobody).")]
        public static bool HasOwnerType(IReadOnlyList<MyCubeGrid> group, string ownerType)
        {
            if (string.IsNullOrEmpty(ownerType))
                return false;

            // Get the owner type of the group.
            var gridOwnerType = Utils.Ownership.GetOwnerType(group);

            // Check provided input string.
            switch (ownerType.ToLower().Trim())
            {
                // Check if grid is owner by an NPC.
                case "npc":
                case "npcs":
                    return gridOwnerType == Utils.Ownership.OwnerType.Npc;

                // Check if the grid is owned by a Player.
                case "player":
                case "players":
                    return gridOwnerType == Utils.Ownership.OwnerType.Player;

                // Check if the grid is owned by Nobody.
                case "nobody":
                    return gridOwnerType == Utils.Ownership.OwnerType.Nobody;
            }

            // In all other cases, just return false.
            return false;
        }

        [Condition("blocksgreaterthan", helpText: "Finds grids with more than the given number of blocks (whole group).")]
        public static bool BlocksGreaterThan(IReadOnlyList<MyCubeGrid> group, int count)
        {
            return group.Sum(g => g.BlocksCount) > count;
        }

        [Condition("haspower", "nopower", "Finds grids with, or without power.")]
        public static bool HasPower(MyCubeGrid grid)
        {
            foreach (var b in grid.GetFatBlocks())
            {
                var c = b.Components?.Get<MyResourceSourceComponent>();
                if (c == null)
                    continue;

                //some sources don't have electricity and Keen apparently doesn't know what TryGetValue is
                if (!c.ResourceTypes.Contains(MyResourceDistributorComponent.ElectricityId))
                    continue;

                if (c.HasCapacityRemainingByType(MyResourceDistributorComponent.ElectricityId) && c.ProductionEnabledByType(MyResourceDistributorComponent.ElectricityId))
                    return true;
            }

            return false;
        }

        [Condition("insideplanet", helpText: "Finds grids that are trapped inside planets.")]
        public static bool InsidePlanet(MyCubeGrid grid)
        {
            var s = grid.PositionComp.WorldVolume;
            var voxels = new List<MyVoxelBase>();
            MyGamePruningStructure.GetAllVoxelMapsInSphere(ref s, voxels);

            if (!voxels.Any())
                return false;

            foreach (var v in voxels)
            {
                var planet = v as MyPlanet;
                if (planet == null)
                    continue;

                var dist2center = Vector3D.DistanceSquared(s.Center, planet.PositionComp.WorldVolume.Center);
                if (dist2center <= (planet.MaximumRadius * planet.MaximumRadius) / 2)
                    return true;
            }

            return false;
        }

        [Condition("playerdistancelessthan", "playerdistancegreaterthan", "Finds grids that are closer (or further) than the given distance from players who are alive in the world.")]
        public static bool PlayerDistanceLessThan(MyCubeGrid grid, double dist)
        {
            dist *= dist;
            foreach (var player in MySession.Static.Players.GetOnlinePlayers())
            {
                if (!TryGetLivePosition(player, out var position))
                    continue;

                if (Vector3D.DistanceSquared(position, grid.PositionComp.GetPosition()) < dist)
                    return true;
            }
            return false;
        }

        [Condition("poweredgriddistancegreaterthan", helpText: "Finds grids that are farther than the given distance from other grids that are powered.")]
        public static bool PoweredGridDistanceGreaterThan(IReadOnlyList<MyCubeGrid> group, double dist)
        {
            // True when no grid outside this group with its power switch on is within the given distance
            // of any grid in this group.
            dist *= dist;
            var own = new HashSet<long>(group.Select(g => g.EntityId));

            foreach (var other in MyEntities.GetEntities().OfType<MyCubeGrid>())
            {
                if (own.Contains(other.EntityId) || !other.IsPowerSwitchOn)
                    continue;

                var otherPosition = other.PositionComp.GetPosition();
                if (group.Any(g => Vector3D.DistanceSquared(otherPosition, g.PositionComp.GetPosition()) < dist))
                    return false;
            }

            return true;
        }

        [Condition("centerdistancelessthan", "centerdistancegreaterthan", "Finds grids that are closer (or further) than the given distance from center.")]
        public static bool CenterDistanceLessThan(MyCubeGrid grid, double dist)
        {
            dist *= dist;

            return Vector3D.DistanceSquared(Vector3D.Zero, grid.PositionComp.GetPosition()) < dist;
        }

        [Condition("ownedby", helpText: "Finds grids owned by the given player. Can specify player name, IdentityId, 'nobody', 'npc', or 'pirates'.")]
        public static bool OwnedBy(IReadOnlyList<MyCubeGrid> group, string str)
        {
            var allOwners = Utils.Ownership.GetAllOwnerIds(group);
            long identityId;

            if (string.Compare(str, "nobody", StringComparison.InvariantCultureIgnoreCase) == 0)
            {
                return allOwners.Count == 0;
            }

            if (string.Compare(str, "npc", StringComparison.InvariantCultureIgnoreCase) == 0)
            {
                var ownerId = Utils.Ownership.GetOwner(group);
                return ownerId != 0 && Utils.Ownership.IsNpcIdentity(ownerId);
            }

            if (string.Compare(str, "pirates", StringComparison.InvariantCultureIgnoreCase) == 0)
            {
                // Pirates is the faction of the pirate identity, regardless of how many members it has.
                identityId = MyPirateAntennas.GetPiratesId();
                var pirateFaction = MySession.Static.Factions.GetPlayerFaction(identityId);
                if (pirateFaction == null)
                    return allOwners.Contains(identityId);

                return allOwners.Any(owner => pirateFaction.Members.ContainsKey(owner));
            }

            var player = Utilities.GetIdentityByNameOrIds(str);
            if (player == null)
            {
                if (long.TryParse(str, out long NPCId))
                {
                    return allOwners.Contains(NPCId);
                }
                return false;
            }
            identityId = player.IdentityId;

            return allOwners.Contains(identityId);
        }


        [Condition("hastype", "notype", "Finds grids containing blocks of the given type.")]
        public static bool BlockType(MyCubeGrid grid, string str)
        {
            return grid.HasBlockType(str);
        }

        [Condition("hastype-fast", "notype-fast", "Finds grids containing blocks of the given type.")]
        public static bool BlockTypeFast(MyCubeGrid grid, string str)
        {
            return grid.HasBlockTypeFast(str);
        }

        [Condition("hassubtype", "nosubtype", "Finds grids containing blocks of the given subtype.")]
        public static bool BlockSubType(MyCubeGrid grid, string str)
        {
            return grid.HasBlockSubtype(str);
        }

        [Condition("hassubtype-fast", "nosubtype-fast", "Finds grids containing blocks of the given subtype.")]
        public static bool BlockSubTypeFast(MyCubeGrid grid, string str)
        {
            return grid.HasBlockSubtypeFast(str);
        }

        [Condition("haspilot", helpText: "Finds grids with pilots")]
        public static bool Piloted(MyCubeGrid grid)
        {
            return grid.GetFatBlocks().OfType<MyShipController>().Any(b => b.Pilot != null);
        }

        /// <summary>
        /// Position of a player who is alive in the world. Players who are dead or on the respawn screen have
        /// no position (SE reports them at 0,0,0).
        /// </summary>
        public static bool TryGetLivePosition(MyPlayer player, out Vector3D position)
        {
            position = default;

            var character = player.Character;
            var entity = player.Controller?.ControlledEntity?.Entity;
            if (character == null || character.IsDead || character.MarkedForClose || entity == null)
                return false;

            position = entity.PositionComp.GetPosition();
            return true;
        }
    }
}
