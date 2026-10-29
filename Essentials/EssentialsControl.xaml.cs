using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Torch.API.Managers;

namespace Essentials
{
    public partial class EssentialsControl : UserControl
    {
        private EssentialsPlugin Plugin { get; }

        public ICollectionView AutoView { get; }
        public ICollectionView SimSpeedView { get; }

        /// <summary>
        /// Triggers offered on the Auto Commands tab. Sim speed events have their own tab.
        /// </summary>
        public IEnumerable<Trigger> AutoTriggers { get; } = Enum.GetValues(typeof(Trigger)).Cast<Trigger>().Where(t => t != Trigger.SimSpeed).ToList();

        public ObservableCollection<OnlinePlayer> OnlinePlayers { get; } = new();

        public ObservableCollection<KnownCommand> KnownCommands { get; } = new() { new KnownCommand("*", "Every command") };

        private Action? _undo;
        private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(15) };

        public EssentialsControl(EssentialsPlugin plugin)
        {
            Plugin = plugin;
            DataContext = plugin.Config;

            AutoView = new CollectionViewSource { Source = plugin.Config.AutoCommands }.View;
            AutoView.Filter = item => item is AutoCommand cmd && cmd.CommandTrigger != Trigger.SimSpeed;

            SimSpeedView = new CollectionViewSource { Source = plugin.Config.AutoCommands }.View;
            SimSpeedView.Filter = item => item is AutoCommand cmd && cmd.CommandTrigger == Trigger.SimSpeed;

            plugin.Config.AutoCommands.CollectionChanged += (_, _) =>
            {
                AutoView.Refresh();
                SimSpeedView.Refresh();
            };

            // Any config change may add, remove or edit a Steam ID.
            plugin.Config.PropertyChanged += (_, _) => UpdatePermissionDuplicates();
            UpdatePermissionDuplicates();

            _statusTimer.Tick += (_, _) => ClearStatus();

            InitializeComponent();
        }

        #region Status and undo

        private void ShowStatus(string message, Action? undo = null)
        {
            StatusText.Text = message;
            _undo = undo;
            UndoButton.Visibility = undo == null ? Visibility.Collapsed : Visibility.Visible;
            _statusTimer.Stop();
            _statusTimer.Start();
        }

        private void ClearStatus()
        {
            _statusTimer.Stop();
            StatusText.Text = "";
            _undo = null;
            UndoButton.Visibility = Visibility.Collapsed;
        }

        private void UndoButton_OnClick(object sender, RoutedEventArgs e)
        {
            var undo = _undo;
            ClearStatus();
            if (undo == null)
                return;

            undo();
            ShowStatus("Restored.");
        }

        /// <summary>
        /// Removes an item and offers Undo in the status bar, which puts it back in the same place.
        /// </summary>
        private void RemoveWithUndo<T>(IList<T> list, T? item, string description, Selector? selector = null,
            Action? beforeRemove = null, Action? beforeRestore = null) where T : class
        {
            if (item == null)
            {
                ShowStatus("Select an item first, then click Remove.");
                return;
            }

            int index = list.IndexOf(item);
            if (index < 0)
                return;

            beforeRemove?.Invoke();
            list.RemoveAt(index);

            ShowStatus($"Removed {description}.", () =>
            {
                beforeRestore?.Invoke();
                list.Insert(Math.Min(index, list.Count), item);
                if (selector != null)
                    selector.SelectedItem = item;
            });
        }

        private static string Named(string? name, string fallback) => string.IsNullOrWhiteSpace(name) ? fallback : $"'{name}'";

        #endregion

        private void SaveButton_OnClick(object sender, RoutedEventArgs e)
        {
            Plugin.Save();
            ShowStatus("Configuration saved.");
        }

        private void AddAutoCommand_OnClick(object sender, RoutedEventArgs e)
        {
            var command = new AutoCommand { Name = "New Command" };
            Plugin.Config.AutoCommands.Add(command);
            AutoCommandsList.SelectedItem = command;
        }

        private void RemoveAutoCommand_OnClick(object sender, RoutedEventArgs e)
        {
            var selected = AutoCommandsList.SelectedItem as AutoCommand;
            var trigger = selected?.CommandTrigger ?? Trigger.Disabled;
            RemoveWithUndo(Plugin.Config.AutoCommands, selected, $"auto command {Named(selected?.Name, "")}", AutoCommandsList,
                beforeRemove: () => selected!.CommandTrigger = Trigger.Disabled,
                beforeRestore: () => selected!.CommandTrigger = trigger);
        }

        private void AddStep_OnClick(object sender, RoutedEventArgs e)
        {
            if (AutoCommandsList.SelectedItem is AutoCommand selected)
                selected.Steps.Add(new AutoCommand.CommandStep());
        }

        private void RemoveStep_OnClick(object sender, RoutedEventArgs e)
        {
            if (AutoCommandsList.SelectedItem is AutoCommand cmd)
                RemoveWithUndo(cmd.Steps, StepsGrid.SelectedItem as AutoCommand.CommandStep, "step", StepsGrid);
        }

        private void MoveStepUp_OnClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AutoCommand.CommandStep step
                && AutoCommandsList.SelectedItem is AutoCommand cmd)
            {
                int index = cmd.Steps.IndexOf(step);
                if (index > 0)
                    cmd.Steps.Move(index, index - 1);
            }
        }

        private void MoveStepDown_OnClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AutoCommand.CommandStep step
                && AutoCommandsList.SelectedItem is AutoCommand cmd)
            {
                int index = cmd.Steps.IndexOf(step);
                if (index >= 0 && index < cmd.Steps.Count - 1)
                    cmd.Steps.Move(index, index + 1);
            }
        }

        private void StepsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (AutoCommandsList.SelectedItem is not AutoCommand cmd
                || StepsGrid.SelectedItem is not AutoCommand.CommandStep step)
                return;

            int index = cmd.Steps.IndexOf(step);
            int newIndex = -1;

            if (e.Key == Key.Up && index > 0)
                newIndex = index - 1;
            else if (e.Key == Key.Down && index < cmd.Steps.Count - 1)
                newIndex = index + 1;

            if (newIndex < 0)
                return;

            cmd.Steps.Move(index, newIndex);
            StepsGrid.SelectedItem = step;
            StepsGrid.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                StepsGrid.UpdateLayout();
                var row = (DataGridRow)StepsGrid.ItemContainerGenerator.ContainerFromItem(step);
                row?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            }));
            e.Handled = true;
        }

        private void AddSimSpeedEvent_OnClick(object sender, RoutedEventArgs e)
        {
            var command = new AutoCommand
            {
                Name = "New Sim Speed Event",
                CommandTrigger = Trigger.SimSpeed,
                SimSpeedDuration = "00:00:05",
                SimSpeedCooldown = "00:00:30"
            };
            Plugin.Config.AutoCommands.Add(command);
            SimSpeedEventsList.SelectedItem = command;
        }

        private void RemoveSimSpeedEvent_OnClick(object sender, RoutedEventArgs e)
        {
            var selected = SimSpeedEventsList.SelectedItem as AutoCommand;
            RemoveWithUndo(Plugin.Config.AutoCommands, selected, $"sim speed event {Named(selected?.Name, "")}", SimSpeedEventsList,
                beforeRemove: () => selected!.CommandTrigger = Trigger.Disabled,
                beforeRestore: () => selected!.CommandTrigger = Trigger.SimSpeed);
        }

        private void AddSimSpeedStep_OnClick(object sender, RoutedEventArgs e)
        {
            if (SimSpeedEventsList.SelectedItem is AutoCommand selected)
                selected.Steps.Add(new AutoCommand.CommandStep());
        }

        private void RemoveSimSpeedStep_OnClick(object sender, RoutedEventArgs e)
        {
            if (SimSpeedEventsList.SelectedItem is AutoCommand cmd)
                RemoveWithUndo(cmd.Steps, SimSpeedStepsGrid.SelectedItem as AutoCommand.CommandStep, "step", SimSpeedStepsGrid);
        }

        private void MoveSimSpeedStepUp_OnClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AutoCommand.CommandStep step
                && SimSpeedEventsList.SelectedItem is AutoCommand cmd)
            {
                int index = cmd.Steps.IndexOf(step);
                if (index > 0)
                    cmd.Steps.Move(index, index - 1);
            }
        }

        private void MoveSimSpeedStepDown_OnClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AutoCommand.CommandStep step
                && SimSpeedEventsList.SelectedItem is AutoCommand cmd)
            {
                int index = cmd.Steps.IndexOf(step);
                if (index >= 0 && index < cmd.Steps.Count - 1)
                    cmd.Steps.Move(index, index + 1);
            }
        }

        private void SimSpeedStepsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (SimSpeedEventsList.SelectedItem is not AutoCommand cmd
                || SimSpeedStepsGrid.SelectedItem is not AutoCommand.CommandStep step)
                return;

            int index = cmd.Steps.IndexOf(step);
            int newIndex = -1;

            if (e.Key == Key.Up && index > 0)
                newIndex = index - 1;
            else if (e.Key == Key.Down && index < cmd.Steps.Count - 1)
                newIndex = index + 1;

            if (newIndex < 0)
                return;

            cmd.Steps.Move(index, newIndex);
            SimSpeedStepsGrid.SelectedItem = step;
            SimSpeedStepsGrid.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                SimSpeedStepsGrid.UpdateLayout();
                var row = (DataGridRow)SimSpeedStepsGrid.ItemContainerGenerator.ContainerFromItem(step);
                row?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            }));
            e.Handled = true;
        }

        private void AddInfoCommand_OnClick(object sender, RoutedEventArgs e)
        {
            var command = new InfoCommand();
            Plugin.Config.InfoCommands.Add(command);
            InfoCommandsGrid.SelectedItem = command;
        }

        private void RemoveInfoCommand_OnClick(object sender, RoutedEventArgs e)
        {
            var selected = InfoCommandsGrid.SelectedItem as InfoCommand;
            RemoveWithUndo(Plugin.Config.InfoCommands, selected, $"info command {Named(selected?.Command, "")}", InfoCommandsGrid);
        }

        private void AddBlockList_OnClick(object sender, RoutedEventArgs e)
        {
            var preset = new BlockList { Name = "New Preset" };
            Plugin.Config.BlockLists.Add(preset);
            BlockListsList.SelectedItem = preset;
        }

        private void RemoveBlockList_OnClick(object sender, RoutedEventArgs e)
        {
            var selected = BlockListsList.SelectedItem as BlockList;
            RemoveWithUndo(Plugin.Config.BlockLists, selected, $"block preset {Named(selected?.Name, "")}", BlockListsList);
        }

        private void AddBlockListItem_OnClick(object sender, RoutedEventArgs e)
        {
            if (BlockListsList.SelectedItem is BlockList selected)
                selected.Items.Add(new BlockListItem());
        }

        private void RemoveBlockListItem_OnClick(object sender, RoutedEventArgs e)
        {
            if (BlockListsList.SelectedItem is BlockList list)
                RemoveWithUndo(list.Items, BlockListItemsGrid.SelectedItem as BlockListItem, "item", BlockListItemsGrid);
        }

        private void AddBlockListCondition_OnClick(object sender, RoutedEventArgs e)
        {
            if (BlockListsList.SelectedItem is BlockList selected)
                selected.Conditions.Add(new BlockListCondition());
        }

        private void RemoveBlockListCondition_OnClick(object sender, RoutedEventArgs e)
        {
            if (BlockListsList.SelectedItem is BlockList list)
                RemoveWithUndo(list.Conditions, BlockListConditionsGrid.SelectedItem as BlockListCondition, "condition", BlockListConditionsGrid);
        }

        private void AddCleanupPreset_OnClick(object sender, RoutedEventArgs e)
        {
            var preset = new CleanupPreset { Name = "New Preset" };
            Plugin.Config.CleanupPresets.Add(preset);
            CleanupPresetsList.SelectedItem = preset;
        }

        private void RemoveCleanupPreset_OnClick(object sender, RoutedEventArgs e)
        {
            var selected = CleanupPresetsList.SelectedItem as CleanupPreset;
            RemoveWithUndo(Plugin.Config.CleanupPresets, selected, $"cleanup preset {Named(selected?.Name, "")}", CleanupPresetsList);
        }

        private void AddCleanupTarget_OnClick(object sender, RoutedEventArgs e)
        {
            if (CleanupPresetsList.SelectedItem is CleanupPreset selected)
                selected.Targets.Add(new CleanupTarget());
        }

        private void RemoveCleanupTarget_OnClick(object sender, RoutedEventArgs e)
        {
            if (CleanupPresetsList.SelectedItem is CleanupPreset preset)
                RemoveWithUndo(preset.Targets, CleanupTargetsGrid.SelectedItem as CleanupTarget, "target", CleanupTargetsGrid);
        }

        private void AddCleanupCondition_OnClick(object sender, RoutedEventArgs e)
        {
            if (CleanupPresetsList.SelectedItem is CleanupPreset selected)
                selected.Conditions.Add(new CleanupCondition());
        }

        private void RemoveCleanupCondition_OnClick(object sender, RoutedEventArgs e)
        {
            if (CleanupPresetsList.SelectedItem is CleanupPreset preset)
                RemoveWithUndo(preset.Conditions, CleanupConditionsGrid.SelectedItem as CleanupCondition, "condition", CleanupConditionsGrid);
        }

        private void AddPlayerPermission_OnClick(object sender, RoutedEventArgs e)
        {
            var entry = new PlayerPermission { PlayerName = "New Player" };
            Plugin.Config.PlayerPermissions.Add(entry);
            PlayerPermissionsList.SelectedItem = entry;
        }

        private void RemovePlayerPermission_OnClick(object sender, RoutedEventArgs e)
        {
            var selected = PlayerPermissionsList.SelectedItem as PlayerPermission;
            RemoveWithUndo(Plugin.Config.PlayerPermissions, selected, $"player {Named(selected?.PlayerName, "")}", PlayerPermissionsList);
        }

        private void AddCommandRule_OnClick(object sender, RoutedEventArgs e)
        {
            if (PlayerPermissionsList.SelectedItem is PlayerPermission selected)
                selected.Rules.Add(new CommandRule());
        }

        private void RemoveCommandRule_OnClick(object sender, RoutedEventArgs e)
        {
            if (PlayerPermissionsList.SelectedItem is PlayerPermission entry)
                RemoveWithUndo(entry.Rules, CommandRulesGrid.SelectedItem as CommandRule, "rule", CommandRulesGrid);
        }

        /// <summary>
        /// Flags every entry after the first that uses the same Steam ID.
        /// </summary>
        private void UpdatePermissionDuplicates()
        {
            try
            {
                var seen = new HashSet<ulong>();
                foreach (var entry in Plugin.Config.PlayerPermissions.ToArray())
                    entry.IsDuplicate = entry.SteamId != 0 && !seen.Add(entry.SteamId);
            }
            catch (InvalidOperationException)
            {
                // The list changed while being read; the change itself triggers another update.
            }
        }

        private void OnlinePlayers_OnDropDownOpened(object sender, EventArgs e)
        {
            OnlinePlayers.Clear();
            try
            {
                var players = Sandbox.Game.World.MySession.Static?.Players.GetOnlinePlayers();
                if (players == null)
                    return;
                foreach (var p in players.Where(p => p.Client != null).OrderBy(p => p.DisplayName))
                    OnlinePlayers.Add(new OnlinePlayer(p.DisplayName, p.Client.SteamUserId));
            }
            catch
            {
                // Player list can change while being read; the next open will try again.
            }
        }

        private void OnlinePlayers_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox box || box.SelectedItem is not OnlinePlayer online)
                return;

            if (PlayerPermissionsList.SelectedItem is PlayerPermission entry)
            {
                entry.PlayerName = online.Name;
                entry.SteamId = online.SteamId;
            }

            box.SelectedItem = null;
        }

        private void CommandBox_OnDropDownOpened(object sender, EventArgs e)
        {
            var cmdMan = Plugin.Torch.CurrentSession?.Managers?.GetManager<Torch.Commands.CommandManager>();
            if (cmdMan == null || KnownCommands.Count > 1)
                return;

            var paths = cmdMan.Commands.WalkTree()
                .Select(node => new KnownCommand(string.Join(" ", node.GetPath()), node.Command?.Description ?? "All commands in this group"))
                .OrderBy(c => c.Path);

            foreach (var command in paths)
                KnownCommands.Add(command);
        }
    }

    public class OnlinePlayer
    {
        public OnlinePlayer(string name, ulong steamId)
        {
            Name = name;
            SteamId = steamId;
        }

        public string Name { get; }
        public ulong SteamId { get; }
        public string Display => $"{Name} ({SteamId})";
    }

    public class KnownCommand
    {
        public KnownCommand(string path, string description)
        {
            Path = path;
            Description = description;
        }

        public string Path { get; }
        public string Description { get; }
    }
}
