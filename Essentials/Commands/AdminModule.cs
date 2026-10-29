using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Sandbox;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game.World;
using Torch.API.Managers;
using Torch.Commands;
using Torch.Commands.Permissions;
using Torch.Mod;
using Torch.Mod.Messages;
using VRage.Game.ModAPI;
using VRageRender.Utils;

namespace Essentials.Commands;

[Category("admin")]
public class AdminModule : CommandModule
{
    [Command("stats", "Get performance statistics of the server")]
    [Permission(MyPromoteLevel.Admin)]
    public void Statistics()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Generic:");
        Stats.Generic.WriteTo(sb);
        sb.AppendLine("Network:");
        Stats.Network.WriteTo(sb);
        sb.AppendLine("Timing:");
        Stats.Timing.WriteTo(sb);

        if (Context.Player?.SteamUserId > 0)
            ModCommunication.SendMessageTo(new DialogMessage("Statistics", null, sb.ToString()) , Context.Player.SteamUserId);
        else
            Context.Respond(sb.ToString());
    }

    [Command("playercount", "Gets or sets the max number of players on the server")]
    [Permission(MyPromoteLevel.Admin)]
    public void PlayerCount(int count = -1)
    {
        if (count == -1)
        {
            Context.Respond($"Max player count: {MyMultiplayer.Static.MemberLimit}. Current online players: {MyMultiplayer.Static.MemberCount - 1}");
            return;
        }

        MyMultiplayer.Static.MemberLimit = count;
        Context.Respond($"Max player count: {MyMultiplayer.Static.MemberLimit}. Current online players: {MyMultiplayer.Static.MemberCount - 1}");
    }

    [Command("playerlist", "Lists current players on the server")]
    [Permission(MyPromoteLevel.Admin)]
    public void ListPlayers()
    {
        if(MySession.Static.Players.GetOnlinePlayerCount() == 0)
        {
            Context.Respond("No players online");
            return;
        }
        StringBuilder sb = new();
        var players = MySession.Static.Players.GetOnlinePlayers();
        if (players.Count == 0)
        {
            Context.Respond("No Players Online");
            return;
        }

        sb.AppendLine($"Found {players.Count} Players on server");
        foreach(var player in players)
        {
            sb.AppendLine($"{player.DisplayName}");
            sb.AppendLine($">PlayerId: {player.Identity.IdentityId}");
            sb.AppendLine($">SteamId: {player.Id.SteamId}");
        }
        if (Context.Player == null)
            Context.Respond(sb.ToString());
        else if (Context?.Player?.SteamUserId > 0)
        {
            ModCommunication.SendMessageTo(new DialogMessage("List of Online Players", $"{players.Count} players Online", sb.ToString()), Context.Player.SteamUserId);
        }
    }


    [Command("runauto", "Runs the auto command with the given commandName immediately")]
    [Permission(MyPromoteLevel.Admin)]
    public void RunAuto(string name)
    {
        var command = FindAutoCommand(name);
        if (command == null)
        {
            Context.Respond($"Couldn't find an auto command with the commandName {name}");
            return;
        }
        Context.Respond($"Running the autocommand {command.Name}");
        command.RunNow();
    }

    private static AutoCommand? FindAutoCommand(string name)
    {
        return EssentialsPlugin.Instance.Config.AutoCommands.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    [Command("runpreset", "Runs the custom block preset with the given preset name immediately")]
    [Permission(MyPromoteLevel.Admin)]
    public void RunPreset(string name)
    {
        var preset = EssentialsPlugin.Instance.Config.BlockLists.FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
        if (preset == null)
        {
            Context.Respond($"Couldn't find a block preset with the name '{name}'");
            return;
        }

        if (preset.Problem is { } problem)
        {
            Context.Respond($"Block preset '{preset.Name}' was not run: {problem}");
            return;
        }

        var action = preset.Action switch
        {
            BlockAction.TurnOn => "on",
            BlockAction.TurnOff => "off",
            BlockAction.Remove => "remove",
            _ => "off"
        };

        var items = preset.Items.ToList();
        var cmdMan = EssentialsPlugin.Instance.CommandManager;
        Context.Respond($"Running block preset '{name}' ({items.Count} items)...");

        Task.Run(() =>
        {
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Value))
                    continue;

                var command = preset.GetCommand(action, item);
                EssentialsPlugin.Instance.Torch.Invoke(() => cmdMan?.HandleCommandFromServer(command));
                System.Threading.Thread.Sleep(item.DelaySpan);
            }
        });
    }

    [Command("runcleanup", "Runs the cleanup preset with the given preset name immediately")]
    [Permission(MyPromoteLevel.Admin)]
    public void RunCleanup(string name)
    {
        var preset = EssentialsPlugin.Instance.Config.CleanupPresets.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (preset == null)
        {
            Context.Respond($"Couldn't find a cleanup preset with the name '{name}'");
            return;
        }

        if (preset.Problem is { } problem)
        {
            Context.Respond($"Cleanup preset '{preset.Name}' was not run: {problem}");
            return;
        }

        var cmdMan = EssentialsPlugin.Instance.CommandManager;

        if (preset.ApplyToAll || preset.Action == CleanupAction.DeleteFloatingObjects)
        {
            var commands = preset.GetCommands();
            foreach (var command in commands)
                cmdMan?.HandleCommandFromServer(command);
            Context.Respond($"Ran cleanup preset '{name}' ({commands.Count} commands).");
        }
        else
        {
            var targets = preset.Targets.ToList();
            Context.Respond($"Running cleanup preset '{name}' ({targets.Count} targets)...");
            Task.Run(() =>
            {
                foreach (var target in targets)
                {
                    var command = preset.BuildCommand(target);
                    EssentialsPlugin.Instance.Torch.Invoke(() => cmdMan?.HandleCommandFromServer(command));
                    System.Threading.Thread.Sleep(target.DelaySpan);
                }
            });
        }
    }

    [Command("cancelauto", "Cancels the auto command with the given commandName immediately")]
    [Permission(MyPromoteLevel.Admin)]
    public void EndAuto(string commandName)
    {
        var command = FindAutoCommand(commandName);
        if (command == null)
        {
            Context.Respond($"Couldn't find an auto command with the commandName {commandName}");
            return;
        }
        Context.Respond($"AutoCommand {command.Name} cancelled");
        command.Cancel();
    }

    [Command("cancelautobyindex", "Cancels the auto command with the given index immediately")]
    [Permission(MyPromoteLevel.Admin)]
    public void EndByOrder(int commandNumber = 0)
    {
        var commands = new List<AutoCommand>(EssentialsPlugin.Instance.Config.AutoCommands.Where(x=>x.IsRunning()));

        if (commands.Count == 0)
        {
            Context.Respond("No active autocommand found");
            return;
        }

        if (commandNumber < 1 || commandNumber > commands.Count)
        {
            Context.Respond($"{commandNumber} number is out of range");
            return;
        }
        var command = commands[commandNumber - 1];
        if (command == null)
        {
            Context.Respond($"Couldn't find an auto command from provided number");
            return;
        }
        Context.Respond($"AutoCommand {command.Name} cancelled");
        command.Cancel();
    }

    [Command("listauto", "Lists all detected autocommands ")]
    [Permission(MyPromoteLevel.Admin)]
    public void ListAuto()
    {
        var commands = EssentialsPlugin.Instance.Config.AutoCommands;
        StringBuilder sb = new StringBuilder();
        int count = 1;

        foreach (var command in commands)
        {
            sb.AppendLine((count++) + " " + command.Name);
        }

        if (Context.Player == null)
        {
            Context.Respond("Current AutoCommands:");
            Context.Respond(sb.ToString());

        }

        else
        {
            ModCommunication.SendMessageTo(new DialogMessage("Current AutoCommands",$"Found {commands.Count} Commands",sb.ToString()), Context.Player.SteamUserId);
        }
    }

    [Command("listrunningauto", "Lists all running autocommands ")]
    [Permission(MyPromoteLevel.Admin)]
    public void ListAutoRunning()
    {
        var commands = new List<AutoCommand>(EssentialsPlugin.Instance.Config.AutoCommands.Where(x=>x.IsRunning()));

        if (commands.Count == 0)
        {
            Context.Respond("No active autocommand found");
            return;
        }
        StringBuilder sb = new StringBuilder();
        int count = 1;

        foreach (var command in commands)
        {
            sb.AppendLine((count++) + " " + command.Name);
        }

        if (Context.Player == null)
        {
            Context.Respond("Current AutoCommands:");
            Context.Respond(sb.ToString());

        }

        else
        {
            ModCommunication.SendMessageTo(new DialogMessage("Current AutoCommands",$"Found {commands.Count} Commands",sb.ToString()), Context.Player.SteamUserId);
        }
    }

    [Command("setrank", "Set the promote level of a player.")]
    [Permission(MyPromoteLevel.Admin)]
    public void SetRank(string playerNameOrId, string rank)
    {
        ulong.TryParse(playerNameOrId, out var id);
        id = Utilities.GetPlayerByNameOrId(playerNameOrId)?.SteamUserId ?? id;

        if (id == 0)
        {
            Context.Respond($"Player '{playerNameOrId}' not found or ID is invalid.");
            return;
        }

        if (!Enum.TryParse<MyPromoteLevel>(rank, true, out var promoteLevel) || promoteLevel > MyPromoteLevel.Admin)
        {
            Context.Respond($"Invalid rank '{rank}'.");
            return;
        }

        MySession.Static.SetUserPromoteLevel(id, promoteLevel);
        Context.Respond($"Player '{playerNameOrId}' promoted to '{promoteLevel}'.");
    }

    [Command("reserve", "Add a player to the reserved slots list.")]
    [Permission(MyPromoteLevel.Admin)]
    public void ReserveSlot(string playerNameOrId)
    {
        ulong.TryParse(playerNameOrId, out var id);
        id = Utilities.GetPlayerByNameOrId(playerNameOrId)?.SteamUserId ?? id;
            
        if (id == 0)
        {
            Context.Respond($"Player '{playerNameOrId}' not found or ID is invalid.");
            return;
        }

        if (MySandboxGame.ConfigDedicated.Reserved.Contains(id))
        {
            Context.Respond($"ID {id} is already reserved.");
            return;
        }
            
        MySandboxGame.ConfigDedicated.Reserved.Add(id);
        MySandboxGame.ConfigDedicated.Save();
        Context.Respond($"ID {id} added to reserved slots.");
    }

    [Command("unreserve", "Remove a player from the reserved slots list.")]
    [Permission(MyPromoteLevel.Admin)]
    public void UnreserveSlot(string playerNameOrId)
    {
        ulong.TryParse(playerNameOrId, out var id);
        id = Utilities.GetPlayerByNameOrId(playerNameOrId)?.SteamUserId ?? id;
            
        if (id == 0)
        {
            Context.Respond($"Player '{playerNameOrId}' not found or ID is invalid.");
            return;
        }
            
        if (!MySandboxGame.ConfigDedicated.Reserved.Contains(id))
        {
            Context.Respond($"ID {id} is already unreserved.");
            return;
        }
            
        MySandboxGame.ConfigDedicated.Reserved.Remove(id);
        MySandboxGame.ConfigDedicated.Save();
        Context.Respond($"ID {id} removed from reserved slots.");
    }

    // Command modules are created per command, so mute state must be static.
    private static readonly Dictionary<ulong, DateTime> _muted = [];
    private static readonly object _muteLock = new();
    private static Timer? _muteTimer;
    private static IChatManagerServer? ChatManager => EssentialsPlugin.Instance.Torch.CurrentSession?.Managers?.GetManager<IChatManagerServer>();

    [Command("mute", "Mutes a user in global chat for the given number of minutes.")]
    public void MuteUser(string user, int timeout = 0)
    {
        var p = Utilities.GetPlayerByNameOrId(user);
        if (p == null)
        {
            Context.Respond($"Could not find user {user}");
            return;
        }


        bool res = ChatManager?.MuteUser(p.SteamUserId) ?? false;

        if(!res)
            Context.Respond($"Failed to mute user {user}. They are already muted.");
        else
            Context.Respond(timeout > 0 ? $"Muted user {p.DisplayName} for {timeout} minute(s)" : $"Muted user {p.DisplayName}");

        if (timeout > 0)
        {
            lock (_muteLock)
            {
                _muted[p.SteamUserId] = DateTime.Now + TimeSpan.FromMinutes(timeout);
                if (_muteTimer == null)
                {
                    _muteTimer = new Timer(1000) { AutoReset = true };
                    _muteTimer.Elapsed += MuteTimerElapsed;
                    _muteTimer.Start();
                }
            }
        }
    }

    private static void MuteTimerElapsed(object sender, ElapsedEventArgs e)
    {
        List<ulong> expired;
        lock (_muteLock)
        {
            expired = _muted.Where(m => m.Value <= DateTime.Now).Select(m => m.Key).ToList();
            foreach (var id in expired)
                _muted.Remove(id);

            if (_muted.Count == 0 && _muteTimer != null)
            {
                _muteTimer.Dispose();
                _muteTimer = null;
            }
        }

        if (expired.Count == 0)
            return;

        // Chat manager state belongs to the game thread.
        EssentialsPlugin.Instance.Torch.Invoke(() =>
        {
            foreach (var id in expired)
                ChatManager?.UnmuteUser(id);
        });
    }

    [Command("unmute", "Removes a chat mute from a user, if they have been muted.")]
    public void UnmuteUser(string user)
    {
        var p = Utilities.GetPlayerByNameOrId(user);
        if (p == null)
        {
            Context.Respond($"Could not find user {user}");
            return;
        }
        bool res = ChatManager?.UnmuteUser(p.SteamUserId) ?? false;
        lock (_muteLock)
            _muted.Remove(p.SteamUserId);

        if (!res)
            Context.Respond($"Failed to unmute user {user}. They are not muted.");
        else
            Context.Respond($"Unmuted user {p.DisplayName}");
    }

    [Command("list mute", "Lists all muted users, an their timeout, if applciable.")]
    public void ListMute()
    {
        var mutedUsers = ChatManager?.MutedUsers.ToList();
        if (mutedUsers == null || mutedUsers.Count == 0)
        {
            Context.Respond("No muted users.");
            return;
        }

        var sb = new StringBuilder();

        foreach (var m in mutedUsers)
        {
            var s = MySession.Static.Players.TryGetIdentityNameFromSteamId(m);
            if(string.IsNullOrEmpty(s))
                s = m.ToString();
            bool f;
            DateTime t;
            lock (_muteLock)
                f = _muted.TryGetValue(m, out t);

            sb.AppendLine($"{s}: {(f ? (t - DateTime.Now).ToString(@"hh\:mm\:ss") : "inf")}");
        }

        if (Context.SentBySelf)
        {
            Context.Respond(sb.ToString());
            return;
        }
        var ms = new DialogMessage("Muted Users", content: sb.ToString());
        ModCommunication.SendMessageTo(ms, Context.Player.SteamUserId);
    }

    [Command("give", "Insert an item with a specific quanity into a players inventory")]
    [Permission(MyPromoteLevel.Admin)]
    public void give(string playerName, string itemType, string item, int quantity) {
        string type = "MyObjectBuilder_" + itemType;
        VRage.Game.MyDefinitionId.TryParse(type, item, out VRage.Game.MyDefinitionId defID);

        if (defID.ToString().Contains("null")) {
            Context.Respond("Invalid item type");
            return;
        }

        if (playerName != "*") {
            var p = Utilities.GetPlayerByNameOrId(playerName);
            if (p == null) {
                Context.Respond("Player not found");
                return;
            }
            Sandbox.Game.MyVisualScriptLogicProvider.AddToPlayersInventory(p.IdentityId, defID, quantity);
            ModCommunication.SendMessageTo(new NotificationMessage($"You have been given {quantity} {item} {itemType}", 5000, "Blue"), p.SteamUserId);
        }

        else {
            foreach (var p in MySession.Static.Players.GetOnlinePlayers()) {
                var player = Utilities.GetPlayerByNameOrId(p.DisplayName);
                if (player == null) continue;
                Sandbox.Game.MyVisualScriptLogicProvider.AddToPlayersInventory(p.Identity.IdentityId, defID, quantity);
                ModCommunication.SendMessageTo(new NotificationMessage($"You have been given {quantity} {item} {itemType}", 5000, "Blue"), player.SteamUserId);
            }
        }
        Context.Respond("Item(s) given!");
    }
}