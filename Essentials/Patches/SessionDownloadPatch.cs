using System.Reflection;
using NLog;
using Sandbox.Engine.Multiplayer;
using Torch.Managers.PatchManager;
using VRage.Game;
using VRage.Game.ObjectBuilders.Components;

namespace Essentials.Patches
{
    /// <summary>
    ///     This is a replacement for the vanilla logic that generates a world save to send to new clients.
    ///     The main focus of this replacement is to drastically reduce the amount of data sent to clients
    ///     (which removes some exploits), and to remove as many allocations as realistically possible,
    ///     in order to speed up the client join process, avoiding lag spikes on new connections.
    /// 
    ///     This code is **NOT** free to use, under the Apache license. You know who this message is for.
    ///
    ///    ----------------------------------------------------------------------------------------------------
    ///     Don't know who that message is for, but you cannot add private code to a public repo.  So it's all
    ///     public code as far as I'm concerned.
    /// </summary>
    public class SessionDownloadPatch
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public static void Patch(PatchContext ctx)
        {
            ctx.GetPattern(typeof(MyMultiplayerServerBase).GetMethod("CleanUpData", BindingFlags.NonPublic | BindingFlags.Static)).Suffixes.Add(typeof(SessionDownloadPatch).GetMethod(nameof(CleanupClientWorld), BindingFlags.NonPublic | BindingFlags.Static));
        }

        private static void CleanupClientWorld(MyObjectBuilder_World worldData, ulong playerId, long senderIdentity)
        {
            /*
             * The entire client join code can be cleaned up massively to reduce server load. However, that needs to be in another plugin or keen
             * 
             * 
             * This is being ran directly after the original cleanup. Original removes:
             * 1. Station store items
             * 2. Player relations (keeps only theirs)
             * 3. Player Faction relations (keeps only theres)
             * 
             */

            //I know ALEs stuff removes this, but lets just add it in essentials too
            foreach (var identity in worldData.Checkpoint.Identities)
            {
                //Clear all put sender identity last death position
                if (identity.IdentityId != senderIdentity)
                    identity.LastDeathPosition = null;
            }

            //I dont trust keen to do it
            worldData.Checkpoint.Gps.Dictionary.TryGetValue(senderIdentity, out MyObjectBuilder_Gps value);
            worldData.Checkpoint.Gps.Dictionary.Clear();
            if (value != null)
            {
                worldData.Checkpoint.Gps.Dictionary.Add(senderIdentity, value);
            }

            foreach (var sessionComponent in worldData.Checkpoint.SessionComponents)
            {

                if (sessionComponent is MyObjectBuilder_SessionComponentResearch component)
                {
                    // Remove everyone elses research shit (quick and dirty)
                    for(int i = component.Researches.Count-1; i >= 0; i--)
                    {
                        if (component.Researches[i].IdentityId == senderIdentity)
                            continue;

                        component.Researches.RemoveAt(i);
                    }
                }
            }

            foreach (var player in worldData.Checkpoint.AllPlayersData.Dictionary)
            {

                if (player.Value.IdentityId == senderIdentity)
                    continue;

                //Clear toolbar junk for other players. Seriously keen what the FUCK
                player.Value.Toolbar = null;
            }
        }
    }
}
