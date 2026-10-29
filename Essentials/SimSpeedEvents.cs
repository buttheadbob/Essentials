using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Timers;
using NLog;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.World;
using Timer = System.Timers.Timer;

namespace Essentials;

public class SimSpeedEvents : IDisposable
{
    private static SimSpeedEvents _instance = null!;
    public static SimSpeedEvents Instance => _instance ??= new SimSpeedEvents();
    private static readonly Logger Log = LogManager.GetLogger("Essentials");

    private Timer? _timer;
    private int _tickQueued;
    private readonly Dictionary<AutoCommand, State> _states = new Dictionary<AutoCommand, State>();

    private class State
    {
        public DateTime ConditionMetAt = DateTime.MinValue;
        public DateTime LastFiredAt = DateTime.MinValue;
        public bool HysteresisCleared = true;
    }

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

    private void TimerElapsed(object sender, ElapsedEventArgs e)
    {
        // Evaluate on the game thread; skip this tick if the previous one has not been processed yet.
        if (Interlocked.Exchange(ref _tickQueued, 1) == 1)
            return;

        EssentialsPlugin.Instance.Torch.Invoke(() =>
        {
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error encountered during sim speed update!");
            }
            finally
            {
                Interlocked.Exchange(ref _tickQueued, 0);
            }
        });
    }

    private void Tick()
    {
        var config = EssentialsPlugin.Instance.Config;
        if (config == null)
            return;

        if (MySession.Static?.Ready != true)
            return;

        var simSpeed = Math.Min(Sync.ServerSimulationRatio, 1);
        var now = DateTime.UtcNow;

        // Snapshot, since the UI can edit the list at any time.
        var commands = config.AutoCommands.Where(c => c.CommandTrigger == Trigger.SimSpeed).ToList();

        foreach (var cmd in commands)
        {
            if (cmd.IsRunning())
                continue;

            if (cmd.Steps.Count == 0)
                continue;

            if (!_states.TryGetValue(cmd, out var state))
            {
                state = new State();
                _states[cmd] = state;
            }

            bool conditionMet = IsConditionMet(cmd, simSpeed);
            bool cooldownElapsed = (now - state.LastFiredAt) >= cmd.SimSpeedCooldownSpan;

            if (!conditionMet)
            {
                state.ConditionMetAt = DateTime.MinValue;

                // Recovery counts at any time after firing, including during the cooldown.
                // The cooldown only delays the next fire.
                if (cmd.HysteresisEnabled && !state.HysteresisCleared && IsRecovered(cmd, simSpeed))
                    state.HysteresisCleared = true;
            }
            else if (cmd.HysteresisEnabled && !state.HysteresisCleared)
            {
                // Still waiting for sim speed to recover past the recovery ratio.
                state.ConditionMetAt = DateTime.MinValue;
            }
            else if (cooldownElapsed)
            {
                if (state.ConditionMetAt == DateTime.MinValue)
                    state.ConditionMetAt = now;

                if ((now - state.ConditionMetAt) >= cmd.SimSpeedDurationSpan)
                {
                    cmd.RunNow();
                    state.LastFiredAt = now;
                    state.ConditionMetAt = DateTime.MinValue;
                    state.HysteresisCleared = !cmd.HysteresisEnabled;
                }
            }
        }

        // Forget state for events that were removed or changed to another trigger.
        var active = new HashSet<AutoCommand>(commands);
        foreach (var key in _states.Keys.Where(k => !active.Contains(k)).ToList())
            _states.Remove(key);
    }

    private static bool IsConditionMet(AutoCommand cmd, float simSpeed)
    {
        return cmd.Compare switch
        {
            Gtl.LessThan => simSpeed < cmd.TriggerRatio,
            Gtl.GreaterThan => simSpeed > cmd.TriggerRatio,
            _ => false
        };
    }

    private static bool IsRecovered(AutoCommand cmd, float simSpeed)
    {
        return cmd.Compare switch
        {
            Gtl.LessThan => simSpeed > cmd.HysteresisRatio,
            Gtl.GreaterThan => simSpeed < cmd.HysteresisRatio,
            _ => true
        };
    }

    public void Dispose()
    {
        Stop();
        _states.Clear();
    }
}
