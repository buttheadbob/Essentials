using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Essentials
{
    public partial class EssentialsControl : UserControl
    {
        private EssentialsPlugin Plugin { get; }

        public EssentialsControl(EssentialsPlugin plugin)
        {
            Plugin = plugin;
            DataContext = plugin.Config;
            InitializeComponent();
        }

        private void SaveButton_OnClick(object sender, RoutedEventArgs e)
        {
            Plugin.Save();
        }

        private void AddAutoCommand_OnClick(object sender, RoutedEventArgs e)
        {
            Plugin.Config.AutoCommands.Add(new AutoCommand());
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
            Plugin.Config.BlockLists.Add(new BlockList());
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
    }
}
