using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using NLog;
using Torch;
using Torch.API;
using Torch.API.Managers;
using Torch.Commands;
using Torch.Server;

namespace Essentials
{
    public class AutoCommand : ViewModel
    {
        private TimeSpan _scheduledTime = TimeSpan.Zero;
        private static readonly Logger Log = LogManager.GetLogger("Essentials");
        private TimeSpan _interval = TimeSpan.Zero;
        private DateTime _nextRun = DateTime.MinValue;
        private DayOfWeek _day = DayOfWeek.All;
        private Trigger _trigger = Trigger.Disabled;
        private Gtl _comparer = Gtl.LessThan;
        private int _currentStep;
        private string _name = null!;
        private float _triggerRatio;
        private double _triggerCount;

        // Guards the schedule and run state, which is used by the timer tick, commands and background runs.
        private readonly object _sync = new();
        private readonly List<CancellationTokenSource> _runs = new();

        private TimeSpan _simSpeedDuration = TimeSpan.FromSeconds(5);
        private TimeSpan _simSpeedCooldown = TimeSpan.FromSeconds(30);
        private bool _hysteresisEnabled;
        private float _hysteresisRatio = 0.7f;

        [XmlIgnore]
        public bool Completed { get; set; }

        public Trigger CommandTrigger
        {
            get => _trigger;
            set
            {
                SetValue(ref _trigger, value);
                ResetSchedule();
            }
        }

        public Gtl Compare
        {
            get => _comparer;
            set => SetValue(ref _comparer, value);
        }

        public string Name
        {
            get => _name;
            set => SetValue(ref _name, value);
        }

        public string Interval
        {
            get => _interval.ToString();
            set
            {
                if (!TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var interval))
                    return;

                _interval = interval;
                OnPropertyChanged();
                ResetSchedule();
            }
        }

        /// <summary>
        /// Restarts the schedule from now. Called whenever the trigger or interval changes, so a command switched
        /// on in the UI never inherits a stale next-run time.
        /// </summary>
        private void ResetSchedule()
        {
            lock (_sync)
            {
                _currentStep = 0;
                _nextRun = _trigger switch
                {
                    Trigger.Timed => DateTime.Now + _interval,
                    Trigger.Scheduled => NextScheduledRun(),
                    _ => DateTime.MinValue
                };
            }
        }

        private DateTime NextScheduledRun()
        {
            var next = DateTime.Now.Date + _interval;
            return next <= DateTime.Now ? next + TimeSpan.FromDays(1) : next;
        }

        public float TriggerRatio
        {
            get => _triggerRatio;
            set => SetValue(ref _triggerRatio, Math.Min(Math.Max(value, 0), 1));
        }

        public double TriggerCount
        {
            get => _triggerCount;
            set => SetValue(ref _triggerCount, Math.Max(0, value));
        }

        [XmlIgnore]
        public TimeSpan SimSpeedDurationSpan => _simSpeedDuration;

        public string SimSpeedDuration
        {
            get => _simSpeedDuration.ToString();
            set
            {
                if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var span))
                    SetValue(ref _simSpeedDuration, span);
            }
        }

        [XmlIgnore]
        public TimeSpan SimSpeedCooldownSpan => _simSpeedCooldown;

        public string SimSpeedCooldown
        {
            get => _simSpeedCooldown.ToString();
            set
            {
                if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var span))
                    SetValue(ref _simSpeedCooldown, span);
            }
        }

        public bool HysteresisEnabled
        {
            get => _hysteresisEnabled;
            set => SetValue(ref _hysteresisEnabled, value);
        }

        public float HysteresisRatio
        {
            get => _hysteresisRatio;
            set => SetValue(ref _hysteresisRatio, Math.Min(Math.Max(value, 0), 1));
        }

        public DayOfWeek DayOfWeek
        {
            get => _day;
            set => SetValue(ref _day, value);
        }

        public ObservableCollection<CommandStep> Steps { get; } = new ObservableCollection<CommandStep>();

        public AutoCommand()
        {
            Steps.CollectionChanged += (_, e) =>
            {
                if (e.NewItems != null)
                    foreach (CommandStep item in e.NewItems)
                        item.PropertyChanged += StepChanged;
                if (e.OldItems != null)
                    foreach (CommandStep item in e.OldItems)
                        item.PropertyChanged -= StepChanged;
                OnPropertyChanged();
            };

            foreach (var step in Steps)
                step.PropertyChanged += StepChanged;
        }

        private void StepChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged();

        public void Update()
        {
            lock (_sync)
            {
                if (DateTime.Now < _nextRun)
                    return;

                switch (CommandTrigger)
                {
                    case Trigger.GridCount:
                    case Trigger.PlayerCount:
                        _nextRun = DateTime.Now + _interval;
                        // Don't restart this command while its previous run is still going, otherwise a condition
                        // that stays true starts a new copy every check.
                        if (_runs.Count == 0)
                            RunNow();
                        return;
                    case Trigger.Scheduled when  DayOfWeek != DayOfWeek.All && DateTime.Now.DayOfWeek != (System.DayOfWeek)(int)DayOfWeek:
                        _nextRun += TimeSpan.FromDays(1);
                        return;
                }

                var steps = Steps.ToList();
                if (steps.Count <= 0)
                    return;

                if (_currentStep >= steps.Count)
                    _currentStep = 0;

                var step = steps[_currentStep];

                step.RunStep();
                _currentStep++;
                // Delays count from when the step actually ran.
                _nextRun = DateTime.Now + step.DelaySpan;

                if (_currentStep < steps.Count) return;
                _currentStep = 0;
                _nextRun = _trigger == Trigger.Scheduled
                        ? NextScheduledRun()
                        : DateTime.Now + _interval;
            }
        }

        public class CommandStep : ViewModel
        {
            internal TimeSpan DelaySpan;
            private string _command = null!;

            public string Delay
            {
                get => DelaySpan.ToString();
                set
                {
                    if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var span))
                        SetValue(ref DelaySpan, span);
                }
            }

            public string Command
            {
                get => _command;
                set => SetValue(ref _command, value);
            }

            public void RunStep()
            {
                if (((TorchServer)EssentialsPlugin.Instance.Torch).State != ServerState.Running)
                    return;

                if (string.IsNullOrEmpty(Command))
                    return;

                EssentialsPlugin.Instance.Torch.Invoke(() =>
                   {
                       var manager = EssentialsPlugin.Instance.Torch.CurrentSession.Managers.GetManager<CommandManager>();
                       manager?.HandleCommandFromServer(Command);
                   });
            }

            public override string ToString()
            {
                return Command;
            }
        }

        /// <summary>
        /// Runs all steps in the background. Each run has its own cancellation token, so several runs of the same
        /// command can exist (for example a manual !admin runauto) without interfering with each other.
        /// </summary>
        internal async void RunNow()
        {
            var cts = new CancellationTokenSource();
            lock (_sync)
                _runs.Add(cts);

            try
            {
                var steps = Steps.ToList();
                await Task.Run(() =>
                {
                    foreach (var step in steps)
                    {
                        if (cts.IsCancellationRequested)
                            break;

                        step.RunStep();

                        // Wakes up early when cancelled.
                        cts.Token.WaitHandle.WaitOne(step.DelaySpan);
                    }
                });
            }
            catch (Exception e)
            {
                Log.Error(e, $"Auto command '{_name}' failed");
            }
            finally
            {
                lock (_sync)
                    _runs.Remove(cts);
                cts.Dispose();
            }
        }

        internal void Cancel()
        {
            Log.Info($"Cancelling autocommand {_name}");
            lock (_sync)
            {
                foreach (var run in _runs)
                    run.Cancel();

                _currentStep = 0;
                _nextRun = _trigger == Trigger.Scheduled
                    ? NextScheduledRun()
                    : DateTime.Now + _interval;
            }
        }

        public override string ToString()
        {
            return $"{Name} : {_trigger.ToString()} : {Steps.Count}";
        }

        internal bool IsRunning()
        {
            lock (_sync)
                return _currentStep > 0 || _runs.Count > 0;
        }
    }
    
    public enum Gtl
    {
        LessThan,
        GreaterThan,
        Equal
    }

    public enum Trigger
    {
        Disabled,
        GridCount,
        OnStart,
        PlayerCount,
        Scheduled,
        SimSpeed,
        Timed,
        Vote
    }

    public enum DayOfWeek
    {
        All = -1,
        Sunday = System.DayOfWeek.Sunday,
        Monday = System.DayOfWeek.Monday,
        Tuesday = System.DayOfWeek.Tuesday,
        Wednesday = System.DayOfWeek.Wednesday,
        Thursday = System.DayOfWeek.Thursday,
        Friday = System.DayOfWeek.Friday,
        Saturday = System.DayOfWeek.Saturday
    }
}
