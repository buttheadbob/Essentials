using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Essentials
{
    public partial class EssentialsControl : UserControl
    {
        private EssentialsPlugin Plugin { get; }

        public ICollectionView AutoView { get; }
        public ICollectionView SimSpeedView { get; }

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

            InitializeComponent();
        }

        private void SaveButton_OnClick(object sender, RoutedEventArgs e)
        {
            Plugin.Save();
        }

        private void AddAutoCommand_OnClick(object sender, RoutedEventArgs e)
        {
            var command = new AutoCommand { Name = "New Command" };
            Plugin.Config.AutoCommands.Add(command);
            AutoCommandsList.SelectedItem = command;
        }

        private void RemoveAutoCommand_OnClick(object sender, RoutedEventArgs e)
        {
            if (AutoCommandsList.SelectedItem is AutoCommand selected)
            {
                selected.CommandTrigger = Trigger.Disabled;
                Plugin.Config.AutoCommands.Remove(selected);
            }
        }

        private void AddStep_OnClick(object sender, RoutedEventArgs e)
        {
            if (AutoCommandsList.SelectedItem is AutoCommand selected)
                selected.Steps.Add(new AutoCommand.CommandStep());
        }

        private void RemoveStep_OnClick(object sender, RoutedEventArgs e)
        {
            if (AutoCommandsList.SelectedItem is AutoCommand cmd && StepsGrid.SelectedItem is AutoCommand.CommandStep step)
                cmd.Steps.Remove(step);
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
            StepsGrid.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new System.Action(() =>
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
            if (SimSpeedEventsList.SelectedItem is AutoCommand selected)
            {
                selected.CommandTrigger = Trigger.Disabled;
                Plugin.Config.AutoCommands.Remove(selected);
            }
        }

        private void AddSimSpeedStep_OnClick(object sender, RoutedEventArgs e)
        {
            if (SimSpeedEventsList.SelectedItem is AutoCommand selected)
                selected.Steps.Add(new AutoCommand.CommandStep());
        }

        private void RemoveSimSpeedStep_OnClick(object sender, RoutedEventArgs e)
        {
            if (SimSpeedEventsList.SelectedItem is AutoCommand cmd && SimSpeedStepsGrid.SelectedItem is AutoCommand.CommandStep step)
                cmd.Steps.Remove(step);
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
            SimSpeedStepsGrid.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new System.Action(() =>
            {
                SimSpeedStepsGrid.UpdateLayout();
                var row = (DataGridRow)SimSpeedStepsGrid.ItemContainerGenerator.ContainerFromItem(step);
                row?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            }));
            e.Handled = true;
        }

        private void AddInfoCommand_OnClick(object sender, RoutedEventArgs e)
        {
            Plugin.Config.InfoCommands.Add(new InfoCommand());
        }

        private void RemoveInfoCommand_OnClick(object sender, RoutedEventArgs e)
        {
            if (InfoCommandsGrid.SelectedItem is InfoCommand selected)
                Plugin.Config.InfoCommands.Remove(selected);
        }

        private void AddBlockList_OnClick(object sender, RoutedEventArgs e)
        {
            var preset = new BlockList { Name = "New Preset" };
            Plugin.Config.BlockLists.Add(preset);
            BlockListsList.SelectedItem = preset;
        }

        private void RemoveBlockList_OnClick(object sender, RoutedEventArgs e)
        {
            if (BlockListsList.SelectedItem is BlockList selected)
                Plugin.Config.BlockLists.Remove(selected);
        }

        private void AddBlockListItem_OnClick(object sender, RoutedEventArgs e)
        {
            if (BlockListsList.SelectedItem is BlockList selected)
                selected.Items.Add(new BlockListItem());
        }

        private void RemoveBlockListItem_OnClick(object sender, RoutedEventArgs e)
        {
            if (BlockListsList.SelectedItem is BlockList list && BlockListItemsGrid.SelectedItem is BlockListItem item)
                list.Items.Remove(item);
        }

        private void AddBlockListCondition_OnClick(object sender, RoutedEventArgs e)
        {
            if (BlockListsList.SelectedItem is BlockList selected)
                selected.Conditions.Add(new BlockListCondition());
        }

        private void RemoveBlockListCondition_OnClick(object sender, RoutedEventArgs e)
        {
            if (BlockListsList.SelectedItem is BlockList list && BlockListConditionsGrid.SelectedItem is BlockListCondition condition)
                list.Conditions.Remove(condition);
        }

        private void AddCleanupPreset_OnClick(object sender, RoutedEventArgs e)
        {
            var preset = new CleanupPreset { Name = "New Preset" };
            Plugin.Config.CleanupPresets.Add(preset);
            CleanupPresetsList.SelectedItem = preset;
        }

        private void RemoveCleanupPreset_OnClick(object sender, RoutedEventArgs e)
        {
            if (CleanupPresetsList.SelectedItem is CleanupPreset selected)
                Plugin.Config.CleanupPresets.Remove(selected);
        }

        private void AddCleanupTarget_OnClick(object sender, RoutedEventArgs e)
        {
            if (CleanupPresetsList.SelectedItem is CleanupPreset selected)
                selected.Targets.Add(new CleanupTarget());
        }

        private void RemoveCleanupTarget_OnClick(object sender, RoutedEventArgs e)
        {
            if (CleanupPresetsList.SelectedItem is CleanupPreset preset && CleanupTargetsGrid.SelectedItem is CleanupTarget target)
                preset.Targets.Remove(target);
        }

        private void AddCleanupCondition_OnClick(object sender, RoutedEventArgs e)
        {
            if (CleanupPresetsList.SelectedItem is CleanupPreset selected)
                selected.Conditions.Add(new CleanupCondition());
        }

        private void RemoveCleanupCondition_OnClick(object sender, RoutedEventArgs e)
        {
            if (CleanupPresetsList.SelectedItem is CleanupPreset preset && CleanupConditionsGrid.SelectedItem is CleanupCondition condition)
                preset.Conditions.Remove(condition);
        }
    }
}
