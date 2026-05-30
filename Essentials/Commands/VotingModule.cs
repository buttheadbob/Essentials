using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Torch.API.Managers;
using Torch.Commands;
using Torch.Commands.Permissions;
using Torch.Mod;
using Torch.Mod.Messages;
using VRage.Game.ModAPI;

namespace Essentials.Commands;

public class VotingModule : CommandModule
{
    public enum Status
    {
        VoteStandby,
        VoteInProgress,
        VoteCancel,

        // Last vote
        VoteFail,
        VoteSuccess
    }

    private static readonly Logger Log = LogManager.GetLogger("Essentials Voting");
    private static AutoCommand? _command;
    private static string? _voteInProgress;
    private static readonly Dictionary<ulong, DateTime> VoteReg = [];
    private static readonly Dictionary<ulong, DateTime> VoteCooldown = [];
    public static Status VoteStatus = Status.VoteStandby;

    //last vote info for debugging
    private static string? _lastVoteName;
    private static Status _voteResult;
    private static double _voteResultPercentage;

    [Command("vote", "starts a vote for a command")]
    [Permission(MyPromoteLevel.None)]
    public void Vote(string name)
    {
        if (Context.Player == null)
        {
            Context.Respond("This is an in-game command");
            return;
        }

        if (VoteStatus == Status.VoteInProgress)
        {
            Context.Respond($"vote for {_voteInProgress} is currently active. Use !yes to vote and !no to retract vote");
            return;
        }

        _command = EssentialsPlugin.Instance.Config.AutoCommands.FirstOrDefault(c => !string.IsNullOrEmpty(c.Name) 
                                                                                     && c.CommandTrigger == Trigger.Vote && c.Name.Equals(name));

        if (_command == null)
        {
            Context.Respond($"Couldn't find any votable command with the name {name}");
            return;
        }

        var steamid = Context.Player.SteamUserId;
        if (VoteCooldown.TryGetValue(steamid, out var activeCooldown))
        {
            var difference = activeCooldown - DateTime.Now;
            if (difference.TotalSeconds > 0)
            {
                Context.Respond($"Cooldown active. You can use this command again in {difference.Minutes:N0} minutes : {difference.Seconds:N0} seconds");
                return;
            }

            VoteCooldown[steamid] = DateTime.Now.AddSeconds(_command.TriggerCount);
        }
        else
            VoteCooldown.Add(steamid, DateTime.Now.AddSeconds(_command.TriggerCount));

        var voteDuration = TimeSpan.Parse(_command.Interval);
        // voting status
        _voteInProgress = name;
        VoteStatus = Status.VoteInProgress;
        VoteYes();
        var sb = new StringBuilder();
        sb.AppendLine($"Voting started for {name} by {Context.Player.DisplayName}.");
        sb.AppendLine("Use !yes to vote and !no to retract your vote");
        ModCommunication.SendMessageToClients(new NotificationMessage(sb.ToString(), 15000, "Blue"));
        //vote countdown
        Task.Run(() =>
        {
            var countdown = VoteCountdown(voteDuration).GetEnumerator();
            using (countdown as IDisposable)
            {
                while (countdown.MoveNext()) 
                    Thread.Sleep(1000);
            }
                
        });

        _lastVoteName = _voteInProgress;
    }

    [Command("vote cancel", "Cancels current vote in progress")]
    [Permission(MyPromoteLevel.Admin)]
    public void VoteCancel()
    {
        if (VoteStatus == Status.VoteInProgress)
            VoteStatus = Status.VoteCancel;
        else
            Context.Respond("A vote is not in progress");
    }

    [Command("vote list", "Lists all possible vote commands")]
    [Permission(MyPromoteLevel.None)]
    public void VoteList()
    {
        StringBuilder sb = new StringBuilder();
        var voteCommands = new List<AutoCommand>(EssentialsPlugin.Instance.Config.AutoCommands.Where(x=>x.CommandTrigger == Trigger.Vote));
        if (Context.Player == null)sb.AppendLine($"Found {voteCommands.Count}");
        var c = 1;
        foreach (var command in voteCommands)
        {
            if (string.IsNullOrEmpty(command.Name)) continue;
            sb.AppendLine($"{c}. {command.Name}");
            c++;
        }

        if (Context.Player == null)
        {
            Context.Respond(sb.ToString());
            return;
        }
        ModCommunication.SendMessageTo(new DialogMessage("Vote Commands", $"Found {voteCommands.Count} vote commands", sb.ToString()),Context.Player.SteamUserId);
    }

    [Command("no", "cancel your casted vote")]
    [Permission(MyPromoteLevel.None)]
    public void VoteNo()
    {
        if (Context.Player == null)
            return;

        if (VoteStatus == Status.VoteStandby)
        {
            Context.Respond("no vote in progress");
            return;
        }

        var steamid = Context.Player.SteamUserId;

        VoteReg.Remove(steamid);
        Context.Respond("your vote has been retracted");
    }

    [Command("yes", "Submit a yes vote")]
    [Permission(MyPromoteLevel.None)]
    public void VoteYes()
    {
        if (Context.Player == null)
            return;
            
        if (_command == null)
            return;

        if (VoteStatus == Status.VoteStandby)
        {
            Context.Respond("no vote in progress");
            return;
        }

        var steamid = Context.Player.SteamUserId;
        if (VoteReg.TryGetValue(steamid, out DateTime lastcommand))
        {
            TimeSpan difference = DateTime.Now - lastcommand;
            TimeSpan voteDuration = TimeSpan.Parse(_command.Interval);
            if (difference.TotalSeconds < voteDuration.TotalSeconds)
            {
                Context.Respond("Your vote has already been submitted.");
                return;
            }

            VoteReg[steamid] = DateTime.Now;
        }
        else
        {
            VoteReg.Add(steamid, DateTime.Now);
        }

        Context.Respond("Your vote has been submitted.");
    }

    //debug
    [Command("vote debug", "prints out info from the voting module")]
    [Permission(MyPromoteLevel.Admin)]
    public void VoteDebgug()
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine($"Current Vote Status: {VoteStatus.ToString()}");
        sb.AppendLine($"Current Vote Name: {_voteInProgress}");
        sb.AppendLine($"Current vote count: {VoteReg.Count}");
        sb.AppendLine();
        sb.AppendLine("Last vote info");
        if (_lastVoteName != null)
            sb.AppendLine($"Last vote: {_lastVoteName}");
        sb.AppendLine($"Last Vote Result: {_voteResult.ToString()}");
        sb.AppendLine($"Last vote percent: {_voteResultPercentage}");
        if (Context.Player == null)
            Context.Respond(sb.ToString());
        else if (Context?.Player?.SteamUserId > 0)
            ModCommunication.SendMessageTo(new DialogMessage("List of Online Players", null, sb.ToString()),
                Context.Player.SteamUserId);
    }

    //vote reset
    [Command("vote reset", "Resets the voting module data including cooldowns")]
    [Permission(MyPromoteLevel.Admin)]
    public void VoteReset()
    {
        if (VoteStatus == Status.VoteInProgress) VoteCancel();
        VoteReg.Clear();
        VoteCooldown.Clear();
        _lastVoteName = null;
        _voteResult = Status.VoteStandby;
        _voteResultPercentage = 0;
        Context.Respond("Vote reset successful");
        Log.Info($"Voting module reset by {Context.Player.DisplayName}");
    }

    //vote countdown
    private IEnumerable VoteCountdown(TimeSpan time)
    {
        for (var i = time.TotalSeconds; i >= 0; i--)
        {
            if (VoteStatus != Status.VoteInProgress || VoteReg.Count < 1)
            {
                Context.Torch.CurrentSession.Managers.GetManager<IChatManagerClient>()
                    .SendMessageAsSelf($"Vote for {_voteInProgress} cancelled");
                _voteResult = Status.VoteCancel;
                VoteEnd();
                yield break;
            }

            if (i >= 60 && i % 60 == 0)
            {
                Context.Torch.CurrentSession.Managers.GetManager<IChatManagerClient>()
                    .SendMessageAsSelf($"Voting for {_voteInProgress} ends in {i / 60} minute{Pluralize(i / 60)}.");
                yield return null;
            }

            else if (i > 0)
            {
                if (i < 11)
                    Context.Torch.CurrentSession.Managers.GetManager<IChatManagerClient>()
                        .SendMessageAsSelf($"Voting for {_voteInProgress} ends in {i} second{Pluralize(i)}.");
                yield return null;
            }
            else
            {
                double vr = (double)VoteReg.Count / Utilities.GetOnlinePlayerCount();
                if (vr >= _command?.TriggerRatio)
                {
                    Context.Torch.CurrentSession.Managers.GetManager<IChatManagerClient>()
                        .SendMessageAsSelf($"Vote for {_voteInProgress} is successful");
                    _voteResult = Status.VoteSuccess;
                    _command.RunNow();
                }
                else if (vr < _command?.TriggerRatio)
                {
                    Context.Torch.CurrentSession.Managers.GetManager<IChatManagerClient>()
                        .SendMessageAsSelf($"Vote for {_voteInProgress} failed");
                    _voteResult = Status.VoteFail;
                }

                _voteResultPercentage = vr * 100;
                VoteEnd();
                yield break;
            }
        }
    }


    public void VoteEnd()
    {
        //Make sure it's all good for next round
        _command = null;
        VoteStatus = Status.VoteStandby;
        _voteInProgress = null;
        VoteReg.Clear();
    }

    private string Pluralize(double num)
    {
        return num == 1 ? "" : "s";
    }
}