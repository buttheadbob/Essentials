using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.Entities;
using VRage.Game.ModAPI;

namespace Essentials;

public class GridFinder
{
    /// <summary>
    /// Returns every grid connected to the given grid, including the grid itself.
    /// Grids without connections are returned on their own.
    /// </summary>
    public static List<MyCubeGrid> GetGroup(MyCubeGrid grid, GridLinkTypeEnum linkType = GridLinkTypeEnum.Mechanical)
    {
        var nodes = new List<MyCubeGrid>();
        MyCubeGridGroups.Static.GetGroups(linkType).GetGroupNodes(grid, nodes);
        return nodes.Where(g => g.Physics != null && !g.MarkedForClose).ToList();
    }

    /// <summary>
    /// Main grid of a group: the grid with the most blocks.
    /// </summary>
    public static MyCubeGrid GetMainGrid(IReadOnlyList<MyCubeGrid> group)
    {
        return group.OrderByDescending(g => g.BlocksCount).First();
    }

    /// <summary>
    /// All grids with physics, grouped by mechanical connection and keyed by the main grid's entity id.
    /// </summary>
    public static Dictionary<long, List<MyCubeGrid>> GetAllGrids()
    {
        HashSet<long> processedGridIds = [];
        Dictionary<long, List<MyCubeGrid>> grids = [];

        foreach (MyCubeGrid grid in MyEntities.GetEntities().OfType<MyCubeGrid>().ToList())
        {
            if (grid.Physics == null || grid.MarkedForClose) continue;
            if (processedGridIds.Contains(grid.EntityId)) continue;

            var group = GetGroup(grid);
            if (group.Count == 0)
                continue;

            foreach (var member in group)
                processedGridIds.Add(member.EntityId);

            grids[GetMainGrid(group).EntityId] = group;
        }

        return grids;
    }
}
