using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.World;

namespace Essentials;

public class SimSpeedEvents : IDisposable
{
    private static SimSpeedEvents _instance = null!;
    public static SimSpeedEvents Instance => _instance ??= new SimSpeedEvents();

    private Timer _timer = null!;
    private readonly Dictionary<AutoCommand, State> _states = new Dictionary<AutoCommand, State>();

    private class State
    {
        public DateTime ConditionMetAt = DateTime.MinValue;
        public DateTime LastFiredAt = DateTime.MinValue;
        public bool HysteresisCleared = true;
    }

    public void Start()
    {
        _timer = new Timer(1000);
        _timer.Elapsed += Tick;
        _timer.AutoReset = true;
        _timer.Start();
    }

    private void Tick(object sender, ElapsedEventArgs e)
    {
        var config = EssentialsPlugin.Instance.Config;
        if (config == null)
            return;

        if (MySession.Static?.Ready != true)
            return;

        var simSpeed = Math.Min(Sync.ServerSimulationRatio, 1);
        var now = DateTime.UtcNow;

        foreach (var cmd in config.AutoCommands)
        {
            if (cmd.CommandTrigger != Trigger.SimSpeed)
                continue;

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

                if (cmd.HysteresisEnabled && !state.HysteresisCleared && cooldownElapsed)
                {
                    if (cmd.Compare == Gtl.LessThan && simSpeed > cmd.HysteresisRatio)
                        state.HysteresisCleared = true;
                    else if (cmd.Compare == Gtl.GreaterThan && simSpeed < cmd.HysteresisRatio)
                        state.HysteresisCleared = true;
                }
            }
            else
            {
                if (!state.HysteresisCleared && cmd.HysteresisEnabled && cooldownElapsed)
                {
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
        }

        CleanupStaleStates();
    }

    private void CleanupStaleStates()
    {
        var activeIds = new HashSet<AutoCommand>(
            EssentialsPlugin.Instance.Config.AutoCommands.Where(c => c.CommandTrigger == Trigger.SimSpeed));

        var stale = _states.Keys.Where(k => !activeIds.Contains(k)).ToList();
        foreach (var key in stale)
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

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null!;
        _states.Clear();
    }
}
