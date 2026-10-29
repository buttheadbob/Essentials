using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;
using NLog;
using Torch;
using Torch.API.Managers;
using Torch.Commands;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;

namespace Essentials;

public class PlayerPermission : ViewModel
{
    public string PlayerName { get; set => SetValue(ref field, value); } = "";

    public ulong SteamId { get; set => SetValue(ref field, value); }

    public ObservableCollection<CommandRule> Rules { get; } = [];

    public PlayerPermission()
    {
        Rules.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
                foreach (CommandRule item in e.NewItems)
                    item.PropertyChanged += RuleChanged;
            if (e.OldItems != null)
                foreach (CommandRule item in e.OldItems)
                    item.PropertyChanged -= RuleChanged;
            OnPropertyChanged();
        };
    }

    public override string ToString() => string.IsNullOrWhiteSpace(PlayerName) ? SteamId.ToString() : PlayerName;

    /// <summary>
    /// Set by the UI when another entry uses the same Steam ID. Only the first entry is used.
    /// </summary>
    [XmlIgnore]
    public bool IsDuplicate { get; set => SetValue(ref field, value); }

    /// <summary>
    /// Things the admin should know about this entry, or null when there is nothing to report.
    /// </summary>
    public string? Warning
    {
        get
        {
            var warnings = new List<string>();
            if (SteamId == 0)
                warnings.Add("Enter the player's Steam ID. This entry does nothing without it.");
            if (IsDuplicate)
                warnings.Add("Another entry above uses this Steam ID. Only the first entry is used.");
            if (Rules.Any(r => r.Access == RuleAccess.Allow && CommandPermissions.Normalize(r.Command) == "*"))
                warnings.Add("An Allow rule of * gives this player every command on the server, including Torch commands such as !stop and !restart.");
            return warnings.Count == 0 ? null : string.Join("\n", warnings);
        }
    }

    protected override void OnPropertyChanged([CallerMemberName] string propName = "")
    {
        base.OnPropertyChanged(propName);
        if (propName != nameof(Warning))
            base.OnPropertyChanged(nameof(Warning));
    }

    private void RuleChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged();
}

public class CommandRule : ViewModel
{
    public string Command { get; set => SetValue(ref field, value); } = "";

    public RuleAccess Access { get; set => SetValue(ref field, value); }

    public override string ToString() => $"{Access}: {Command}";
}

public enum RuleAccess
{
    Allow,
    Deny
}

public static class CommandPermissions
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Hooked to <see cref="CommandManager.OnCommandExecuting"/>. Only called for commands typed in chat by players,
    /// so the server console is never affected.
    /// </summary>
    public static void OnCommandExecuting(Command command, IMyPlayer player, bool hasPermission, ref bool? hasPermissionOverride)
    {
        var config = EssentialsPlugin.Instance.Config;
        if (!config.EnableCommandPermissions)
            return;

        RuleAccess? access;
        try
        {
            access = GetAccess(config, player.SteamUserId, string.Join(" ", command.Path));
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to evaluate command permissions");
            return;
        }

        switch (access)
        {
            case RuleAccess.Allow:
                hasPermissionOverride = true;
                break;
            case RuleAccess.Deny:
                hasPermissionOverride = false;
                Log.Info($"{player.DisplayName} was denied command '{string.Join(" ", command.Path)}' by a command permission rule");
                EssentialsPlugin.Instance.Torch.CurrentSession?.Managers?.GetManager<IChatManagerServer>()?
                    .SendMessageAsOther("Server", "You do not have permission to use that command.", Color.Red, player.SteamUserId, MyFontEnum.Red);
                break;
        }
    }

    /// <summary>
    /// Returns the access the most specific matching rule grants, or null when no rule matches.
    /// When an Allow and a Deny rule are equally specific, Deny wins.
    /// </summary>
    public static RuleAccess? GetAccess(EssentialsConfig config, ulong steamId, string commandPath)
    {
        var entry = config.PlayerPermissions.ToArray().FirstOrDefault(p => p.SteamId == steamId);
        if (entry == null)
            return null;

        RuleAccess? result = null;
        int bestScore = -1;

        foreach (var rule in entry.Rules.ToArray())
        {
            int score = MatchScore(Normalize(rule.Command), commandPath);
            if (score < 0)
                continue;

            if (score > bestScore || (score == bestScore && rule.Access == RuleAccess.Deny))
            {
                bestScore = score;
                result = rule.Access;
            }
        }

        return result;
    }

    public static string Normalize(string command)
    {
        command = command.Trim().TrimStart('!');
        return string.Join(" ", command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
    }

    // -1 = no match, 0 = wildcard, otherwise the length of the matched rule (longer = more specific)
    private static int MatchScore(string rule, string commandPath)
    {
        if (rule.Length == 0)
            return -1;

        if (rule == "*")
            return 0;

        if (commandPath.Equals(rule, StringComparison.OrdinalIgnoreCase)
            || commandPath.StartsWith(rule + " ", StringComparison.OrdinalIgnoreCase))
            return rule.Length;

        return -1;
    }
}
