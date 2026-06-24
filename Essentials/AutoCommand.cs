using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
        private bool _isRunning;

        private TimeSpan _simSpeedDuration = TimeSpan.FromSeconds(5);
        private TimeSpan _simSpeedCooldown = TimeSpan.FromSeconds(30);
        private bool _hysteresisEnabled;
        private float _hysteresisRatio = 0.7f;

        [XmlIgnore]
        public bool Completed { get; set; }

        public Trigger CommandTrigger
        {
            get => _trigger;
            set => SetValue(ref _trigger, value);
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
                _interval = TimeSpan.Parse(value);
                OnPropertyChanged();
                if (CommandTrigger == Trigger.Timed)
                {
                    _nextRun = DateTime.Now + _interval;
                }

                if (CommandTrigger == Trigger.Scheduled)
                {
                    _nextRun = DateTime.Now.Date + _interval;
                    if (_nextRun < DateTime.Now) _nextRun += TimeSpan.FromDays(1);
                }
            }
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
            set => SetValue(ref _simSpeedDuration, TimeSpan.Parse(value));
        }

        [XmlIgnore]
        public TimeSpan SimSpeedCooldownSpan => _simSpeedCooldown;

        public string SimSpeedCooldown
        {
            get => _simSpeedCooldown.ToString();
            set => SetValue(ref _simSpeedCooldown, TimeSpan.Parse(value));
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
            if (DateTime.Now < _nextRun)
                return;

            switch (CommandTrigger)
            {
                case Trigger.GridCount:
                case Trigger.PlayerCount:
                    RunNow();
                    _nextRun = DateTime.Now + _interval;
                    return;
                case Trigger.Scheduled when  DayOfWeek != DayOfWeek.All && DateTime.Now.DayOfWeek != (System.DayOfWeek)(int)DayOfWeek:
                    _nextRun += TimeSpan.FromDays(1);
                    return;
            }

            if (Steps.Count <= 0)
                return;

            var step = Steps[_currentStep];

            step.RunStep();
            _currentStep++;
            _nextRun += step.DelaySpan;

            if (_currentStep < Steps.Count) return;
            _currentStep = 0;
            _cTokenSource?.Dispose();
            _nextRun = _trigger == Trigger.Scheduled
                    ? DateTime.Now.Date + _interval + TimeSpan.FromDays(1)
                    : _nextRun = DateTime.Now + _interval;
        }

        public class CommandStep : ViewModel
        {
            internal TimeSpan DelaySpan;
            private string _command = null!;

            public string Delay
            {
                get => DelaySpan.ToString();
                set => SetValue(ref DelaySpan, TimeSpan.Parse(value));
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

        private CancellationTokenSource? _cTokenSource;

        internal async void RunNow()
        {
            _cTokenSource = new CancellationTokenSource();
            var token = _cTokenSource.Token;
            _isRunning = true;
            var steps = new List<CommandStep>(Steps);
            await Task.Run(() =>
            {
                foreach (var step in steps)
                {
                    if (token.IsCancellationRequested)
                    {
                        break;
                    }
                    step.RunStep();
                    Thread.Sleep(step.DelaySpan);
                }
            }, token);
            
            _cTokenSource!.Dispose();
            _isRunning = false;
        }

        internal void Cancel()
        {
            Log.Info($"Cancelling autocommand {_name}");
            _cTokenSource?.Cancel();
            _currentStep = 0;
            _nextRun = _trigger == Trigger.Scheduled
                ? DateTime.Now.Date + _interval + TimeSpan.FromDays(1)
                : _nextRun = DateTime.Now + _interval;
        }

        public override string ToString()
        {
            return $"{Name} : {_trigger.ToString()} : {Steps.Count}";
        }

        internal bool IsRunning()
        {
            return _currentStep > 0 || _isRunning;
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
