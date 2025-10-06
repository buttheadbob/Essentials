using System;
using Sandbox.Game.Entities;
using System.Collections.Generic;
using System.Linq;
using VRage.Collections;
using VRage.Game.ModAPI;
using VRage.Groups;
using VRageMath;

namespace Essentials;

public class GridFinder 
{
    public static (long, List<MyCubeGrid>) FindGridGroupMechanical(string gridNameOrId) 
    {
        Dictionary<long, List<MyCubeGrid>> grids = GetAllGrids();
        KeyValuePair<long, List<MyCubeGrid>> kvp = grids.FirstOrDefault(x => x.Value.Any(y => y.DisplayName.Equals(gridNameOrId, StringComparison.OrdinalIgnoreCase) || y.EntityId.ToString() == gridNameOrId));

        return (kvp.Key, kvp.Value);
    }

    public static (long, List<MyCubeGrid>) FindLookAtGridGroupMechanical(IMyCharacter controlledEntity) 
    {
        const float range = 5000;
        Matrix worldMatrix;
        Vector3D startPosition;
        Vector3D endPosition;

        worldMatrix = controlledEntity.GetHeadMatrix(true, true, false); // dead center of player cross-hairs, or the direction the player is looking with ALT.
        startPosition = worldMatrix.Translation + worldMatrix.Forward * 0.5f;
        endPosition = worldMatrix.Translation + worldMatrix.Forward * (range + 0.5f);

        (double,IMyCubeGrid?) closestGrid = (-1, null);
        RayD ray = new (startPosition, worldMatrix.Forward);

        foreach (IMyCubeGrid? cubeGrid in MyEntities.GetEntities().OfType<IMyCubeGrid>()) 
        {
            if (cubeGrid?.Physics == null)
                continue;

            // check if the ray comes anywhere near the Grid before continuing.    
            if (!ray.Intersects(cubeGrid.WorldAABB).HasValue)
                continue;

            Vector3I? hit = cubeGrid.RayCastBlocks(startPosition, endPosition);
            if (!hit.HasValue)
                continue;

            double distance = (startPosition - cubeGrid.GridIntegerToWorld(hit.Value)).Length();

            if (closestGrid.Item2 is null)
            {
                closestGrid.Item1 = distance;
                closestGrid.Item2 = cubeGrid;
                continue;
            }

            if (distance < closestGrid.Item1)
            {
                closestGrid.Item1 = distance;
                closestGrid.Item2 = cubeGrid;
            }
        }

        // No grids nearby
        if (closestGrid.Item2 is null)
            return (0, []);
        
        return FindGridGroupMechanical(closestGrid.Item2.DisplayName);
    }

    public static Dictionary<long, List<MyCubeGrid>> GetAllGrids()
    {
        HashSet<long> ProcessedGridIds = [];
        Dictionary<long, List<MyCubeGrid>> grids = [];
        
        // Get grid groups, if any. (If a grid has no connections to another grid, it will not be in a group)
        HashSetReader<MyGroups<MyCubeGrid, MyGridMechanicalGroupData>.Group> mechGroups = MyCubeGridGroups.Static.Mechanical.Groups;
        foreach (MyGroups<MyCubeGrid, MyGridMechanicalGroupData>.Group? group in mechGroups)
        {
            List<MyCubeGrid> connectedGrids = [];
            foreach (MyGroups<MyCubeGrid, MyGridMechanicalGroupData>.Node? grid in group.Nodes)
            {
                if (grid?.NodeData.Physics == null) continue;
                if (grid.NodeData.MarkedForClose) continue;
                
                connectedGrids.Add(grid.NodeData);
                ProcessedGridIds.Add(grid.NodeData.EntityId);
            }
            
            MyCubeGrid? biggy = connectedGrids.OrderByDescending(x => x.BlocksCount).First();
            if (biggy is null) continue; // Something fishy with this group, move on.

            grids.Add(biggy.EntityId, connectedGrids);
        }
        
        // Get all grids and sort out grid groups
        foreach (MyCubeGrid? grid in MyEntities.GetEntities().OfType<MyCubeGrid>())
        {
            if (grid is not null)
            {
                long id = grid.EntityId;
                if (ProcessedGridIds.Contains(id)) continue;
                grids.Add(grid.EntityId, [grid]);
            }
        }

        return grids;
    }
}