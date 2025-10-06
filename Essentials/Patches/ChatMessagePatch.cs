
using System.Reflection;
using Torch.Managers.PatchManager;
using NLog;
using VRage.Network;
using Sandbox.Engine.Multiplayer;

namespace Essentials.Patches;

[PatchShim]
public static class ChatMessagePatch 
{
    public static PlayerAccountModule PlayerAccountData = new ();
    public static RanksAndPermissionsModule RanksAndPermissions = new ();
    public static bool debug = false;

    public static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public static MethodInfo? FindOverLoadMethod( MethodInfo[] methodInfo,string name, int parameterLength) 
    {
        foreach (MethodInfo DeclaredMethod in methodInfo)
        {
            if (debug)
                Log.Info($"Method name: {DeclaredMethod.Name}");

            if (DeclaredMethod.GetParameters().Length == parameterLength && DeclaredMethod.Name == name)
                return DeclaredMethod;
        }
        
        return null;
    }

    public static void Patch(PatchContext ctx) 
    {
        try 
        {
            MethodInfo? target = FindOverLoadMethod(typeof(MyMultiplayerBase).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static), "OnChatMessageReceived_Server", 1);
            MethodInfo? patchMethod = typeof(ChatMessagePatch).GetMethod(nameof(OnChatMessageReceived_Server), BindingFlags.Static | BindingFlags.NonPublic);
            ctx.GetPattern(target).Prefixes.Add(patchMethod);

            Log.Info("Patched OnChatMessageReceived_Server!");
        }
        catch 
        {
            Log.Error("Failed to patch!");
        }
    }

    // What is this for? It doesn't seem to do anything.
    private static bool OnChatMessageReceived_Server(ref ChatMsg msg) 
    {
        if (EssentialsPlugin.Instance.Config.EnableRanks) 
        {
            var Account = PlayerAccountData.GetAccount(msg.Author);
            if (Account != null) 
            {
                var Rank = RanksAndPermissions.GetRankData(Account.Rank); // <-- Why?
            }
        }
        
        return true;
    }
}