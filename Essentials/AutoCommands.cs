using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Timers;
using Timer = System.Timers.Timer;
using NLog;
using Torch.Server.ViewModels;
using Sandbox.Game.World;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.Entities;
using VRage.Game.ModAPI;
using static Essentials.Gtl;


namespace Essentials;

public class AutoCommands : IDisposable
{
    private static AutoCommands _instance = null!;
    public static AutoCommands Instance => _instance ?? (_instance = new AutoCommands());
    private static readonly Logger Log = LogManager.GetLogger("Essentials");
    private Timer? _timer;
    private int _tickQueued;

    public void Start()
    {
        if (_timer != null)
            return;

        _timer = new Timer(1000);
        _timer.Elapsed += TimerElapsed;
        _timer.AutoReset = true;
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private bool CanRun(AutoCommand command)
    {
        if (MySession.Static?.Ready != true) return false;

        switch (command.CommandTrigger)
        {
            case Trigger.Disabled:
                return  false;
            case Trigger.OnStart:
                if (command.Completed || command.IsRunning())break;
                command.Completed = true;
                command.RunNow();
                break;
            case Trigger.Vote:
                break;
            case Trigger.Timed:
                return true;
            case Trigger.Scheduled:
                return true;
            case Trigger.GridCount:
                var gridCount = MyEntities.GetEntities().OfType<IMyCubeGrid>().Count();
                switch (command.Compare)
                {
                    case GreaterThan:
                        return gridCount > command.TriggerCount;
                    case LessThan:
                        return gridCount < command.TriggerCount;
                    case Equal:
                        return Math.Abs(gridCount-command.TriggerCount)<1;
                    default:
                        return false;
                }
            case Trigger.PlayerCount:

                switch (command.Compare)
                {
                    case GreaterThan:
                        return Utilities.GetOnlinePlayerCount() > command.TriggerCount;
                    case LessThan:
                        return Utilities.GetOnlinePlayerCount() < command.TriggerCount;
                    case Equal:
                        return Math.Abs(Utilities.GetOnlinePlayerCount() - command.TriggerCount) < 1;
                }
                break;

            case Trigger.SimSpeed:
                return false;
                            
            default:
                return false;
        }

        return false;
    }


    private void TimerElapsed(object sender, ElapsedEventArgs e)
    {
        // Run the check on the game thread, where entities, players and commands are safe to use.
        // Skip this tick if the previous one has not been processed yet.
        if (Interlocked.Exchange(ref _tickQueued, 1) == 1)
            return;

        EssentialsPlugin.Instance.Torch.Invoke(() =>
        {
            try
            {
                Tick();
            }
            finally
            {
                Interlocked.Exchange(ref _tickQueued, 0);
            }
        });
    }

    private void Tick()
    {
        AutoCommand[] commands;
        try
        {
            // Snapshot, since the UI can edit the list at any time.
            commands = EssentialsPlugin.Instance.Config.AutoCommands.ToArray();
        }
        catch (Exception)
        {
            return;
        }

        foreach (var command in commands)
        {
            try
            {
                if(!CanRun(command))
                    continue;

                command.Update();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error encountered during autocommand update!");
            }
        }
    }

    public void Dispose()
    {
        Stop();
    }
}