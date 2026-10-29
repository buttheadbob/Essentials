using Essentials.Utils;
using NLog;
using Sandbox;
using Sandbox.Engine.Multiplayer;
using Sandbox.Engine.Voxels;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Game.World;
using Sandbox.Game.World.Generator;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Shapes;
using Torch.Commands;
using Torch.Commands.Permissions;
using Torch.Mod;
using Torch.Mod.Messages;
using Torch.Utils;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Network;
using VRage.Utils;
using VRage.Voxels;
using VRageMath;
using static Sandbox.Game.Entities.MyVoxelBase;
using Parallel = ParallelTasks.Parallel;
#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value

namespace Essentials.Commands;

[Category("voxels")]
public class VoxelModule : CommandModule
{
    private static Logger _log = LogManager.GetCurrentClassLogger();

    [ReflectedGetter(Name = "m_asteroidsModule")]
    private static Func<MyProceduralWorldGenerator, MyProceduralAsteroidCellGenerator>? _asteroidGenerator;
        
    [ReflectedSetter(Name = "m_isClosingEntities")]
    private static Action<MyProceduralAsteroidCellGenerator, bool>? _deletingSet;

    private static MyProceduralAsteroidCellGenerator? _generatorInstance;
        
    private static MyProceduralAsteroidCellGenerator? GeneratorInstance => _generatorInstance ?? (_generatorInstance = _asteroidGenerator!(MyProceduralWorldGenerator.Static));

    [Command("reset all", "Resets all voxel maps.")]
    public void ResetAll(bool deleteStorage = false)
    {
        var voxelMaps = MyEntities.GetEntities().OfType<MyVoxelBase>();

        var resetIds = new List<long>();
        int count = 0;

        foreach (var map in voxelMaps)
        {
            try
            {
                if (map.StorageName == null || map.Storage.DataProvider == null)
                    continue;

                count++;

                long id = map.EntityId;

                if (deleteStorage && map is MyVoxelMap)
                {
                    using (PinDelete())
                        map.Close();
                }
                else
                {
                    map.Storage.Reset(MyStorageDataTypeFlags.All);
                    resetIds.Add(id);
                }
            }
            catch (Exception e)
            {
                _log.Error($"{e.Message}\n{e.StackTrace}");
            }
        }

        ModCommunication.SendMessageToClients(new VoxelResetMessage(resetIds.ToArray()));

        Context.Respond($"Reset {count} voxel maps.");
    }

    [Command("cleanup asteroids", "Resets all asteroids that don't have a grid or player nearby.")]
    public void CleanupAsteroids(bool deleteStorage = false)
    {
        var voxelMaps = MyEntities.GetEntities().OfType<MyVoxelMap>();

        var resetIds = new List<long>();
        int count = 0;

        foreach (var map in voxelMaps)
        {
            try
            {
                if (map.StorageName == null || map.Storage.DataProvider == null)
                    continue;

                var s = map.PositionComp.WorldVolume;
                var nearEntities = new List<MyEntity>();

                MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref s, nearEntities);

                if (nearEntities.Any(e => e is MyCubeGrid || e is MyCharacter))
                    continue;

                count++;

                long id = map.EntityId;

                if (deleteStorage)
                {
                    using (PinDelete())
                        map.Close();
                }
                else
                {
                    map.Storage.Reset(MyStorageDataTypeFlags.All);
                    resetIds.Add(id);
                }
            }
            catch (Exception e)
            {
                _log.Error($"{e.Message}\n{e.StackTrace}");
            }
        }

        ModCommunication.SendMessageToClients(new VoxelResetMessage(resetIds.ToArray()));

        Context.Respond($"Reset {count} voxel maps.");
    }

    [Command("cleanup distant", "Resets all asteroids that don't have a grid or player inside the specified radius.")]
    public void CleanupAsteroidsDistant(double distance = 1000, bool deleteStorage = false)
    {

        var voxelMaps = MyEntities.GetEntities().OfType<MyVoxelMap>();

        var resetIds = new List<long>();
        int count = 0;

        foreach (var map in voxelMaps)
        {
            try
            {
                if (map.StorageName == null || map.Storage.DataProvider == null)
                    continue;

                var s = new BoundingSphereD(map.PositionComp.GetPosition(), distance);
                var nearEntities = new List<MyEntity>();

                MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref s, nearEntities);

                if (nearEntities.Any(e => e is MyCubeGrid || e is MyCharacter))
                    continue;

                count++;

                long id = map.EntityId;

                if (deleteStorage)
                {
                    using (PinDelete())
                        map.Close();
                }
                else
                {
                    map.Storage.Reset(MyStorageDataTypeFlags.All);
                    resetIds.Add(id);
                }
            }
            catch (Exception e)
            {
                _log.Error($"{e.Message}\n{e.StackTrace}");
            }
        }

        ModCommunication.SendMessageToClients(new VoxelResetMessage(resetIds.ToArray()));

        Context.Respond($"Reset {count} voxel maps.");
    }

    [Command("reset planets", "Resets all planets.")]
    public void ResetPlanets()
    {
        var voxelMaps = MyEntities.GetEntities().OfType<MyPlanet>();

        var resetIds = new List<long>();

        foreach (var map in voxelMaps)
        {
            try
            {
                if (map.StorageName == null || map.Storage.DataProvider == null)
                    continue;

                map.Storage.Reset(MyStorageDataTypeFlags.All);
                resetIds.Add(map.EntityId);
            }
            catch (Exception e)
            {
                _log.Error($"{e.Message}\n{e.StackTrace}");
            }
        }

        ModCommunication.SendMessageToClients(new VoxelResetMessage(resetIds.ToArray()));
        Context.Respond($"Reset {resetIds.Count} voxel maps.");
    }

    [Command("reset planet", "Resets the planet with a given name.")]
    public void ResetPlanet(string planetName)
    {
        var maps = new List<MyPlanet>(2);
        foreach (MyPlanet map in MyEntities.GetEntities().OfType<MyPlanet>())
        {
            if (map.StorageName?.Contains(planetName, StringComparison.CurrentCultureIgnoreCase) == true)
                maps.Add(map);
        }

        switch (maps.Count)
        {
            case 0:
                Context.Respond($"Couldn't find planet with name {planetName}");
                return;
            case 1:
                var map = maps[0];
                map.Storage.Reset(MyStorageDataTypeFlags.All);
                ModCommunication.SendMessageToClients(new VoxelResetMessage(new long[] { map.EntityId }));
                Context.Respond($"Reset planet {map.Name}");
                return;
            default:
                Context.Respond($"Found {maps.Count} planets matching '{planetName}'. Please select from list:");
                Context.Respond(string.Join("\r\n", maps.Select(m => m.StorageName)));
                return;
        }
    }

    [Command("reset area", "Resets voxel damange in specified radius from player")]
    [Permission(MyPromoteLevel.Admin)]
    public void ResetVoxelArea(float radius)
    {
        if (Context.Player == null)
        {
            Context.Respond("This command must be used in-game. From the console use: !voxels reset gps <x y z> <radius>");
            return;
        }

        var entity = Context.Player.Controller?.ControlledEntity?.Entity;
        if (entity == null || Context.Player.Character == null || Context.Player.Character.IsDead)
        {
            Context.Respond("You must be alive in the world to use this command.");
            return;
        }

        if (radius <= 0)
        {
            Context.Respond("Invalid radius!");
            return;
        }

        Context.Respond(ResetVoxelInArea(entity.PositionComp.GetPosition(), radius)
            ? "Voxel reset complete!" 
            : "Couldn't reset voxels! Check log for more information!");
    }

    [Command("reset gps", "Resets voxel damange in specified radius from given point. Usage: !voxels reset gps <x y z | x:y:z | GPS:Name:x:y:z:> <radius>")]
    [Permission(MyPromoteLevel.Admin)]
    public void ResetVoxelGps()
    {
        // Args are parsed manually so coordinates can be given as "x y z", "x:y:z" or a pasted GPS string
        const string usage = "Usage: !voxels reset gps <x y z | x:y:z | GPS:Name:x:y:z:> <radius>";
        var args = Context.Args;

        if (args.Count < 2)
        {
            Context.Respond(usage);
            return;
        }

        if (!double.TryParse(args[args.Count - 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var radius) || radius <= 0)
        {
            Context.Respond("Invalid radius!");
            return;
        }

        if (!TryParsePosition(string.Join(" ", args.Take(args.Count - 1)), out var resetTarget))
        {
            Context.Respond($"Invalid coordinates! {usage}");
            return;
        }

        Context.Respond(ResetVoxelInArea(resetTarget, (float)radius)
            ? "Voxel reset complete!"
            : "Couldn't reset voxels! Check log for more information!");
    }

    private static bool TryParsePosition(string text, out Vector3D position)
    {
        position = Vector3D.Zero;
        text = text.Trim();

        string[] parts;
        if (text.StartsWith("GPS:", StringComparison.OrdinalIgnoreCase))
        {
            // GPS:Name:X:Y:Z:#Color:
            var gpsParts = text.Split(':');
            if (gpsParts.Length < 5)
                return false;
            parts = new[] { gpsParts[2], gpsParts[3], gpsParts[4] };
        }
        else
        {
            parts = text.Split(new[] { ':', ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        }

        if (parts.Length != 3)
            return false;

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
            || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
            return false;

        position = new Vector3D(x, y, z);
        return true;
    }

    public static bool ResetVoxelInArea(Vector3D center, float radius, bool updatePlayers = true)
    {
        try
        {
            BoundingSphereD Sphere = new BoundingSphereD(center, radius);
            // Use our own list: MyEntities.GetEntitiesInSphere returns SE's shared list, which callers must clear.
            var found = new List<MyEntity>();
            MyGamePruningStructure.GetAllEntitiesInSphere(ref Sphere, found);
            List<MyVoxelBase> Maps = found.OfType<MyVoxelBase>().ToList();
            if (Maps.Count == 0)
                return true;

            var m = typeof(MyVoxelBase).GetMethod("PerformVoxelOperationSphere_Implementation", BindingFlags.Instance | BindingFlags.NonPublic);

            foreach (var voxelMap in Maps)
            {
                if (voxelMap.MarkedForClose)
                    continue;

                MyShapeSphere shape = new()
                {
                    Center = center,
                    Radius = radius
                };

                MyVoxelGenerator.RevertShape(voxelMap, shape);

                if (updatePlayers)
                    Events.RaiseEvent<MyVoxelBase, Vector3D, float, byte, OperationType>(voxelMap.RootVoxel, m, shape.Center, shape.Radius, 0, MyVoxelBase.OperationType.Revert);

            }
            return true;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Voxel reset failed!");
            return false;
        }
    }

    private static LockToken? PinDelete()
    {
        /*
         * If SEWorldGenerator Plugin is used or Procedural Seed is 0 there
         * will be no GeneratorInstance. In this case this Method will NRE.
         *
         * However we still would like to delete stuff. So just return null.
         * Because when there is no GeneratorInstance we dont have to tell it
         * entities are being deleted.
         */
        if (GeneratorInstance == null)
            return null;

        return new LockToken();
    }

    private class LockToken : IDisposable
    {
        public LockToken()
        {
            _deletingSet?.Invoke(GeneratorInstance!, true);
        }

        public void Dispose()
        {
            _deletingSet?.Invoke(GeneratorInstance!, false);
        }
    }
}