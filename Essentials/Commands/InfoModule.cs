using System;
using System.Linq;
using Sandbox.Game;
using Sandbox.Game.World;
using Torch.Commands;
using Torch.API.Managers;
using Torch.Mod;
using Torch.Mod.Messages;
using VRage.Game;
using VRageMath;

namespace Essentials.Commands;

[Category("info")]
public class InfoModule:CommandModule
{
    public static void Init()
    {
        IChatManagerServer? c = EssentialsPlugin.Instance.Torch.CurrentSession?.Managers?.GetManager<IChatManagerServer>();
        c?.MessageProcessing += MessageProcessing;
    }

    [Command("list", "Lists all available info commands.")]
    public void List()
    {
        Context.Respond(string.Join(", ", EssentialsPlugin.Instance.Config.InfoCommands.Select(i => i.Command).Where(c => !string.IsNullOrEmpty(c))));
    }

    private static void MessageProcessing(TorchChatMessage msg, ref bool consumed)
    {
        InfoCommand[] infoCommands;
        try
        {
            // Snapshot, since the UI can edit the list at any time.
            infoCommands = EssentialsPlugin.Instance.Config.InfoCommands.ToArray();
        }
        catch (Exception)
        {
            return;
        }

        if (infoCommands.Length == 0)
            return;

        var text = msg.Message?.Trim();
        var c = infoCommands.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.Command)
                                                 && string.Equals(i.Command!.Trim(), text, StringComparison.OrdinalIgnoreCase));
        if (c == null)
            return;

        consumed = true;
        if (msg.AuthorSteamId != null)
        {
            long playerId = MySession.Static.Players.TryGetIdentityId(msg.AuthorSteamId.Value);

            if (!string.IsNullOrEmpty(c.ChatResponse))
                EssentialsPlugin.Instance.Torch.CurrentSession?.Managers?.GetManager<IChatManagerServer>()?.SendMessageAsOther("Server", c.ChatResponse, Color.Blue, msg.AuthorSteamId.Value, MyFontEnum.Blue);
            
            if (!string.IsNullOrEmpty(c.DialogResponse))
                ModCommunication.SendMessageTo(new DialogMessage(c.Command, content: c.DialogResponse), msg.AuthorSteamId.Value);
            
            if (!string.IsNullOrEmpty(c.URL))
                MyVisualScriptLogicProvider.OpenSteamOverlay($"https://steamcommunity.com/linkfilter/?url={c.URL}", playerId);
        }
    }
}