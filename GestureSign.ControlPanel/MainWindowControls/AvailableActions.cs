using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using GestureSign.Common.Gestures;
using GestureSign.Common.Localization;
using GestureSign.ControlPanel.Common;
using GestureSign.ControlPanel.Dialogs;
using GestureSign.ControlPanel.ViewModel;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace GestureSign.ControlPanel.MainWindowControls
{
    /// <summary>
    /// AvailableActions.xaml 的交互逻辑
    /// </summary>
    public partial class AvailableActions : UserControl
    {
        private bool _isUpdatingToggleAllSwitch;

        // public static event EventHandler StartCapture;
        public AvailableActions()
        {
            InitializeComponent();
            DataContext = this;
        }

        private IApplication _cutActionSource;
        private readonly List<CommandInfo> _commandClipboard = new List<CommandInfo>();

        // Drag and drop support for actions
        private Point _actionDragStartPoint;
        private IAction _draggedAction;
        private bool _dropProcessed; // Prevents multiple Drop events in single drag

        // Drag and drop support for commands
        private Point _commandDragStartPoint;
        private CommandInfo _draggedCommand;

        private void UserControl_Initialized(object sender, EventArgs eArgs)
        {
            ApplicationManager.Instance.CollectionChanged += (o, e) =>
            {
                if (e.NewItems != null && e.NewItems.Count > 0 && !(e.NewItems[0] is IgnoredApp))
                    lstAvailableApplication.SelectedItem = (IApplication)e.NewItems[0];
            };
        }

        private void cmdEditCommand_Click(object sender, RoutedEventArgs e)
        {
            EditCommand();
        }

        private void EditCommand()
        {
            // Make sure at least one item is selected
            if (lstAvailableActions.SelectedItems.Count == 0) return;

            // Get first item selected, associated action, and selected application
            CommandInfo selectedItem = (CommandInfo)lstAvailableActions.SelectedItem;
            var selectedAction = selectedItem.Action;
            var selectedCommand = selectedItem.Command;
            if (selectedCommand == null) return;

            var selectedApp = lstAvailableApplication.SelectedItem as IApplication;

            // Get CommandInfoProvider correctly (it's wrapped in ObjectDataProvider)
            var objectDataProvider = Resources["CommandInfoProvider"] as System.Windows.Data.ObjectDataProvider;
            var commandInfoProvider = objectDataProvider?.ObjectInstance as CommandInfoProvider;

            System.Diagnostics.Debug.WriteLine($"[AvailableActions.EditCommand] ========== BEGIN EDIT COMMAND ==========");
            System.Diagnostics.Debug.WriteLine($"[AvailableActions.EditCommand] Selected Command: {selectedCommand.PluginClass}");
            System.Diagnostics.Debug.WriteLine($"[AvailableActions.EditCommand] Selected Action: {selectedAction.GestureName}");
            System.Diagnostics.Debug.WriteLine($"[AvailableActions.EditCommand] Before Edit - Action order: {string.Join(", ", selectedApp.Actions.Select(a => a.GestureName))}");
            System.Diagnostics.Debug.WriteLine($"[AvailableActions.EditCommand] Before Edit - CommandInfos count: {commandInfoProvider?.CommandInfos.Count ?? 0}");

            // Get ListCollectionView for monitoring
            var lcv = System.Windows.Data.CollectionViewSource.GetDefaultView(lstAvailableActions.ItemsSource) as System.Windows.Data.ListCollectionView;
            System.Diagnostics.Debug.WriteLine($"[AvailableActions.EditCommand] ListCollectionView Groups count: {lcv?.Groups?.Count ?? 0}");

            CommandDialog commandDialog = new CommandDialog(selectedCommand, selectedAction);
            var result = commandDialog.ShowDialog();
            if (result != null && result.Value)
            {
                System.Diagnostics.Debug.WriteLine($"[AvailableActions.EditCommand] User confirmed changes");
                // No need to Remove+Insert anymore - Command.PropertyChanged will update UI automatically
                ApplicationManager.Instance.SaveApplications();
                System.Diagnostics.Debug.WriteLine($"[AvailableActions.EditCommand] ========== END EDIT COMMAND ==========");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[AvailableActions.EditCommand] User cancelled changes");
            }
        }

        private void cmdDeleteCommand_Click(object sender, RoutedEventArgs e)
        {
            // Verify that we have an item selected
            if (lstAvailableActions.SelectedItems.Count == 0) return;

            // Confirm user really wants to delete selected items
            if (UIHelper.GetParentWindow(this)
                    .ShowModalMessageExternal(LocalizationProvider.Instance.GetTextValue("Action.Messages.DeleteConfirmTitle"),
                      string.Format(LocalizationProvider.Instance.GetTextValue("Action.Messages.DeleteCommandConfirm"), lstAvailableActions.SelectedItems.Count),
                        MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings()
                        {
                            AffirmativeButtonText = LocalizationProvider.Instance.GetTextValue("Common.OK"),
                            NegativeButtonText = LocalizationProvider.Instance.GetTextValue("Common.Cancel"),
                            ColorScheme = MetroDialogColorScheme.Accented,
                        }) != MessageDialogResult.Affirmative)
                return;

            var commandInfoList = lstAvailableActions.SelectedItems.Cast<CommandInfo>().ToList();
            IApplication selectedApp = lstAvailableApplication.SelectedItem as IApplication;

            // Group by action to handle empty actions correctly
            var actionsToRemove = new List<IAction>();

            // Loop through selected commands
            for (int i = commandInfoList.Count - 1; i >= 0; i--)
            {
                // Grab selected item
                CommandInfo selectedCommand = commandInfoList[i];

                // RemoveCommand will trigger CollectionChanged event
                // which will automatically update CommandInfos collection
                selectedCommand.Action.RemoveCommand(selectedCommand.Command);

                // Track actions that become empty
                if (selectedCommand.Action.IsEmpty() && !actionsToRemove.Contains(selectedCommand.Action))
                {
                    actionsToRemove.Add(selectedCommand.Action);
                }
            }

            // Remove empty actions after all commands are removed
            foreach (var action in actionsToRemove)
            {
                selectedApp.RemoveAction(action);
            }

            // Save entire list of applications
            ApplicationManager.Instance.SaveApplications();
        }

        private void lstAvailableActions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            EnableRelevantButtons();
        }

        private void lstAvailableActions_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                switch (e.Key)
                {
                    case Key.C:
                        SetClipboardAction();
                        _cutActionSource = null;
                        e.Handled = true;
                        break;
                    case Key.X:
                        if (SetClipboardAction())
                            _cutActionSource = (IApplication)lstAvailableApplication.SelectedItem;
                        e.Handled = true;
                        break;
                    case Key.V:
                        if (_commandClipboard.Count > 0)
                        {
                            var selectedCommand = lstAvailableActions.SelectedItem as CommandInfo;
                            if (selectedCommand?.Action != null)
                                PasteToSelectedActionMenuItem_Click(sender, null);
                            else
                                PasteToNewActionMenuItem_Click(sender, null);
                        }
                        e.Handled = true;
                        break;
                }
            }
        }

        // Disabled: This event handler was causing scroll issues by dynamically modifying margins
        // which changed total content height and made scrollbar unstable
        //private void LstAvailableActions_OnScrollChanged(object sender, ScrollChangedEventArgs e)
        //{
        //    HitTestResult hitTest = VisualTreeHelper.HitTest(lstAvailableActions, new Point(5, 5));
        //    var element = hitTest.VisualHit as UIElement;
        //    if (element != null)
        //    {
        //        Rect bounds = element.TransformToAncestor(lstAvailableActions).TransformBounds(new Rect(0.0, 0.0, element.RenderSize.Width, element.RenderSize.Height));
        //        var gestureImageContainer = element.FindChild<Grid>("GestureImageGrid");
        //        if (gestureImageContainer == null) return;
        //        if (bounds.Top < 0)
        //        {
        //            var topMargin = -bounds.Top + gestureImageContainer.ActualHeight > element.RenderSize.Height
        //                ? element.RenderSize.Height - gestureImageContainer.ActualHeight
        //                : Math.Abs(bounds.Top);
        //            gestureImageContainer.Margin = new Thickness(0, topMargin, 0, 0);
        //        }
        //        else gestureImageContainer.Margin = new Thickness(0);
        //    }
        //}

        private void CommandCheckBox_Click(object sender, RoutedEventArgs e)
        {
            CommandInfo info = UIHelper.GetParentDependencyObject<ListBoxItem>(sender as ToggleSwitch).Content as CommandInfo;
            if (info == null) return;
            info.Command.IsEnabled = (sender as ToggleSwitch).IsOn;
            ApplicationManager.Instance.SaveApplications();
        }

        private void ActionToggleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            var toggleSwitch = sender as ToggleSwitch;
            var groupItem = UIHelper.GetParentDependencyObject<GroupItem>(toggleSwitch);
            var group = groupItem?.Content as CollectionViewGroup;
            if (group?.Name is IAction action)
            {
                action.IsEnabled = toggleSwitch.IsOn;
                ApplicationManager.Instance.SaveApplications();
            }
        }

        private void btnAddAction_Click(object sender, RoutedEventArgs e)
        {
            var selectedApplication = lstAvailableApplication.SelectedItem as IApplication;
            if (selectedApplication == null)
            {
                lstAvailableApplication.SelectedIndex = 0;
                selectedApplication = lstAvailableApplication.SelectedItem as IApplication;
                if (selectedApplication == null) return;
            }
            var ci = lstAvailableActions.SelectedItem as CommandInfo;
            if (ci == null)
            {
                var newCommand = new Command();
                Dispatcher.Invoke(() =>
                {
                    lstAvailableActions.SelectedItem = null;
                    var newAction = new GestureSign.Common.Applications.Action();
                    newAction.AddCommand(newCommand);
                    selectedApplication.AddAction(newAction);
                    ApplicationManager.Instance.SaveApplications();
                }, DispatcherPriority.Input);
            }
            else
            {
                var element = (FrameworkElement)sender;
                element.ContextMenu.PlacementTarget = element;
                element.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
                element.ContextMenu.IsOpen = true;
            }
        }

        private void NewCommandMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var selectedApplication = lstAvailableApplication.SelectedItem as IApplication;
            if (selectedApplication == null)
            {
                lstAvailableApplication.SelectedIndex = 0;
                selectedApplication = lstAvailableApplication.SelectedItem as IApplication;
                if (selectedApplication == null) return;
            }

            var newCommand = new Command();
            Dispatcher.Invoke(() =>
            {
                lstAvailableActions.SelectedItem = null;
                var newAction = new GestureSign.Common.Applications.Action();
                newAction.AddCommand(newCommand);
                selectedApplication.AddAction(newAction);
                ApplicationManager.Instance.SaveApplications();
            }, DispatcherPriority.Input);
        }

        private void FromSelectedMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var ci = lstAvailableActions.SelectedItem as CommandInfo;
            if (ci == null) return;

            var newCommand = new Command();
            lstAvailableActions.SelectedItem = null;
            int commandIndex = ci.Action.Commands.ToList().IndexOf(ci.Command);
            ci.Action.InsertCommand(commandIndex + 1, newCommand);
            ApplicationManager.Instance.SaveApplications();
        }

        private void EnableRelevantButtons()
        {
            cmdDelete.IsEnabled = cmdEdit.IsEnabled = lstAvailableActions.SelectedItems.Count != 0;

            var selectedInfo = (lstAvailableActions.SelectedItem as CommandInfo);
            if (selectedInfo == null)
                MoveUpButton.IsEnabled = MoveDownButton.IsEnabled = false;
            else
            {
                int index = selectedInfo.Action.Commands.ToList().IndexOf(selectedInfo.Command);

                MoveUpButton.IsEnabled = index > 0;
                MoveDownButton.IsEnabled = index < selectedInfo.Action.Commands.Count() - 1;
            }
        }

        private bool SetClipboardAction()
        {
            _commandClipboard.Clear();
            foreach (CommandInfo commandInfo in lstAvailableActions.SelectedItems)
            {
                if (commandInfo?.Command != null)
                    _commandClipboard.Add(commandInfo);
            }
            return _commandClipboard.Count != 0;
        }

        private void ActionButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            // Handle both Button click and MenuItem click
            Button button;
            if (sender is MenuItem menuItem)
            {
                var contextMenu = menuItem.Parent as ContextMenu;
                button = contextMenu?.PlacementTarget as Button;
            }
            else
            {
                button = sender as Button;
            }

            if (button == null) return;

            List<CommandInfo> infoList = new List<CommandInfo>();
            var groupItem = UIHelper.GetParentDependencyObject<GroupItem>(button);
            var collectionViewGroup = groupItem?.Content as CollectionViewGroup;
            if (collectionViewGroup == null || collectionViewGroup.Items.Count == 0) return;

            lstAvailableActions.SelectedItems.Clear();
            foreach (CommandInfo item in collectionViewGroup.Items)
            {
                lstAvailableActions.SelectedItems.Add(item);
                infoList.Add(item);
            }

            if (infoList.Count == 0) return;
            var sourceAction = infoList.First().Action;
            var selectedApplication = lstAvailableApplication.SelectedItem as IApplication;
            if (selectedApplication == null)
            {
                selectedApplication = ApplicationManager.Instance.GetAvailableUserApplications().FirstOrDefault(app => app.Actions.Contains(sourceAction));
                if (selectedApplication == null)
                    return;
            }

            System.Diagnostics.Debug.WriteLine($"[AvailableActions] Before ActionDialog: sourceAction={sourceAction.GetHashCode()}, gesture={sourceAction.GestureName}");
            System.Diagnostics.Debug.WriteLine($"[AvailableActions] Actions order before: {string.Join(", ", selectedApplication.Actions.Select(a => $"{a.GestureName}({a.GetHashCode()})"))}");

            ActionDialog actionDialog = new ActionDialog(sourceAction, selectedApplication);
            var result = actionDialog.ShowDialog();

            if (result != null && result.Value)
            {
                var newAction = actionDialog.NewAction;

                System.Diagnostics.Debug.WriteLine($"[AvailableActions] After ActionDialog: newAction={newAction.GetHashCode()}, gesture={newAction.GestureName}, sourceAction={sourceAction.GetHashCode()}");
                System.Diagnostics.Debug.WriteLine($"[AvailableActions] newAction != sourceAction: {newAction != sourceAction}");
                System.Diagnostics.Debug.WriteLine($"[AvailableActions] Actions order after dialog: {string.Join(", ", selectedApplication.Actions.Select(a => $"{a.GestureName}({a.GetHashCode()})"))}");

                if (newAction != sourceAction)
                {
                    // Switching to different gesture: replace sourceAction position with newAction
                    System.Diagnostics.Debug.WriteLine($"[AvailableActions] Actions are different, replacing sourceAction with newAction");
                    lstAvailableActions.SelectedItem = null;

                    // Move commands from source to new action
                    foreach (CommandInfo info in infoList)
                    {
                        sourceAction.RemoveCommand(info.Command);
                        newAction.AddCommand(info.Command);
                    }

                    // Replace sourceAction with newAction at the same position
                    int sourceIndex = selectedApplication.Actions.ToList().IndexOf(sourceAction);
                    System.Diagnostics.Debug.WriteLine($"[AvailableActions] sourceIndex={sourceIndex}");
                    selectedApplication.RemoveAction(newAction); // Remove the one added by ActionDialog (at end)
                    selectedApplication.Insert(sourceIndex, newAction); // Insert at source position
                    selectedApplication.RemoveAction(sourceAction); // Remove old action
                    System.Diagnostics.Debug.WriteLine($"[AvailableActions] Actions order after replace: {string.Join(", ", selectedApplication.Actions.Select(a => $"{a.GestureName}({a.GetHashCode()})"))}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[AvailableActions] Actions are same, no reordering needed");
                }
                // else: Editing existing gesture, ActionDialog has already updated properties
                // No need to Remove/Insert, which would trigger unnecessary CollectionChanged events

                ApplicationManager.Instance.SaveApplications();
                System.Diagnostics.Debug.WriteLine($"[AvailableActions] Actions order after save: {string.Join(", ", selectedApplication.Actions.Select(a => $"{a.GestureName}({a.GetHashCode()})"))}");

                // Scroll to the edited action to ensure thumbnail update
                ScrollToAction(newAction);
            }
        }

        /// <summary>
        /// Scrolls to the specified Action to ensure its thumbnail is rendered and updated
        /// </summary>
        private void ScrollToAction(IAction action)
        {
            if (action == null)
                return;

            Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    // CommandInfoProvider is wrapped in ObjectDataProvider
                    var objectDataProvider = Resources["CommandInfoProvider"] as System.Windows.Data.ObjectDataProvider;
                    if (objectDataProvider == null)
                        return;

                    var commandInfoProvider = objectDataProvider.ObjectInstance as CommandInfoProvider;
                    if (commandInfoProvider == null)
                        return;

                    var commandInfo = commandInfoProvider.CommandInfos.FirstOrDefault(ci => ci.Action == action);

                    if (commandInfo != null)
                    {
                        lstAvailableActions.ScrollIntoView(commandInfo);
                        lstAvailableActions.UpdateLayout(); // Force layout update

                        // Force refresh the ListCollectionView to ensure GroupItems are rendered
                        var lcv = System.Windows.Data.CollectionViewSource.GetDefaultView(lstAvailableActions.ItemsSource);
                        if (lcv != null)
                        {
                            lcv.Refresh();

                            // Wait for one frame to allow rendering to complete
                            await System.Threading.Tasks.Task.Delay(100);
                            lstAvailableActions.UpdateLayout();
                        }
                    }
                }
                catch (Exception ex)
                {
                    GestureSign.Common.Log.Logging.LogException(ex);
                }
            }, DispatcherPriority.ContextIdle);  // Use ContextIdle to wait for rendering to complete
        }

        private void ExportActionMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ExportImportDialog exportImportDialog = new ExportImportDialog(true, false, ApplicationManager.Instance.Applications, GestureSign.Common.Gestures.GestureManager.Instance.Gestures);
            exportImportDialog.ShowDialog();
        }

        private void lstAvailableApplication_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            PasteActionMenuItem2.IsEnabled = _commandClipboard.Count != 0;

            DeleteMenuItem.IsEnabled = lstAvailableApplication.SelectedItem is UserApp;
        }

        private void LstAvailableActions_OnContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            PasteToNewActionMenuItem.Visibility = PasteToSelectedActionMenuItem.Visibility = _commandClipboard.Count != 0 ? Visibility.Visible : Visibility.Collapsed;
            CopyActionMenuItem.IsEnabled = CutActionMenuItem.IsEnabled = lstAvailableActions.SelectedIndex != -1;
        }

        private void CutActionMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (SetClipboardAction())
                _cutActionSource = (IApplication)lstAvailableApplication.SelectedItem;
        }

        private void CopyActionMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SetClipboardAction();
            _cutActionSource = null;
        }

        private void PasteToNewActionMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_commandClipboard.Count == 0) return;

            var targetApplication = lstAvailableApplication.SelectedItem as IApplication;
            if (targetApplication == null) return;

            lstAvailableActions.SelectedItem = null;
            foreach (var actionGroup in _commandClipboard.GroupBy(ci => ci.Action))
            {
                var sourceAction = (GestureSign.Common.Applications.Action)actionGroup.Key;
                var newAction = sourceAction.DeepCopy();
                newAction.Commands = new List<GestureSign.Common.Applications.ICommand>();

                targetApplication.AddAction(newAction);

                foreach (var info in actionGroup)
                {
                    if (_cutActionSource != null)
                    {
                        info.Action.RemoveCommand(info.Command);
                    }

                    var newCommand = ((Command)info.Command).Clone() as Command;
                    newAction.AddCommand(newCommand);
                }
            }

            if (_cutActionSource != null)
            {
                _cutActionSource = null;
                _commandClipboard.Clear();
            }

            ApplicationManager.Instance.SaveApplications();
        }

        private void PasteToSelectedActionMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_commandClipboard.Count == 0) return;

            var targetApplication = lstAvailableApplication.SelectedItem as IApplication;
            if (targetApplication == null) return;
            var selectedCommand = lstAvailableActions.SelectedItem as CommandInfo;
            if (selectedCommand == null || selectedCommand.Action == null) return;
            lstAvailableActions.SelectedItem = null;

            IAction currentAction = selectedCommand.Action;
            foreach (var actionGroup in _commandClipboard.GroupBy(ci => ci.Action))
            {
                foreach (var info in actionGroup)
                {
                    if (_cutActionSource != null)
                    {
                        info.Action.RemoveCommand(info.Command);
                    }

                    var newCommand = ((Command)info.Command).Clone() as Command;

                    currentAction.AddCommand(newCommand);
                }
            }

            if (_cutActionSource != null)
            {
                _cutActionSource = null;
                _commandClipboard.Clear();
            }

            ApplicationManager.Instance.SaveApplications();
        }


        private void lstAvailableApplication_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 0) return;
            IApplication selectedApp = lstAvailableApplication.SelectedItem as IApplication;
            if (selectedApp == null)
            {
                ToggleAllActionsToggleSwitch.IsEnabled = false;
                ContinuousGesturePanel.Visibility = Visibility.Collapsed;
                return;
            }

            UpdateContinuousGestureModeComboBox(selectedApp);

            var commandInfoProvider = ((ObjectDataProvider)Resources["CommandInfoProvider"]).ObjectInstance as CommandInfoProvider;
            if (commandInfoProvider == null) return;
            commandInfoProvider.RefreshCommandInfos(selectedApp, lstAvailableActions);

            // Keep two-level grouping (FingerCount -> Action)
            // Sort groups by FingerCount ascending, then by Order to preserve command sequence
            var lcv = lstAvailableActions.ItemsSource as ListCollectionView;
            if (lcv != null)
            {
                lcv.SortDescriptions.Clear();
                lcv.GroupDescriptions.Clear();
                lcv.SortDescriptions.Add(new SortDescription(nameof(CommandInfo.FingerCount), ListSortDirection.Ascending));
                lcv.SortDescriptions.Add(new SortDescription(nameof(CommandInfo.Order), ListSortDirection.Ascending)); // Secondary sort by Order
                lcv.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CommandInfo.FingerCount)));

                // Enable live sorting so UI updates when Order property changes
                var liveShaping = lcv as ICollectionViewLiveShaping;
                if (liveShaping != null && liveShaping.CanChangeLiveSorting)
                {
                    liveShaping.IsLiveSorting = true;
                    liveShaping.LiveSortingProperties.Clear();
                    liveShaping.LiveSortingProperties.Add(nameof(CommandInfo.Order));
                }
                lcv.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CommandInfo.Action)));
                lcv.Refresh();
            }

            ToggleAllActionsToggleSwitch.IsEnabled = true;
            _isUpdatingToggleAllSwitch = true;
            try
            {
                ToggleAllActionsToggleSwitch.IsOn = selectedApp.Actions.All(a => a.IsEnabled);
            }
            finally
            {
                _isUpdatingToggleAllSwitch = false;
            }

            Dispatcher.InvokeAsync(() => lstAvailableApplication.ScrollIntoView(selectedApp), DispatcherPriority.Background);
        }

        private void NewApplicationButton_OnClick(object sender, RoutedEventArgs e)
        {
            ApplicationDialog applicationDialog = new ApplicationDialog(new UserApp(), true);
            applicationDialog.ShowDialog();
        }

        private void EditApplication_Click(object sender, RoutedEventArgs e)
        {
            EditApplication();
        }

        private void EditApplication()
        {
            var app = lstAvailableApplication.SelectedItem as IApplication;
            if (app != null)
            {
                ApplicationDialog applicationDialog = new ApplicationDialog(app);
                applicationDialog.ShowDialog();
            }
        }

        private void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var selectedApp = lstAvailableApplication.SelectedItem as UserApp;
            if (selectedApp != null && UIHelper.GetParentWindow(this)
                .ShowModalMessageExternal(
                    LocalizationProvider.Instance.GetTextValue("Action.Messages.DeleteConfirmTitle"),
                    String.Format(LocalizationProvider.Instance.GetTextValue("Action.Messages.DeleteAppConfirm"), selectedApp.Name),
                    MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings()
                    {
                        AffirmativeButtonText = LocalizationProvider.Instance.GetTextValue("Common.OK"),
                        NegativeButtonText = LocalizationProvider.Instance.GetTextValue("Common.Cancel"),
                        ColorScheme = MetroDialogColorScheme.Accented,
                    }) == MessageDialogResult.Affirmative)
            {
                ApplicationManager.Instance.RemoveApplication(selectedApp);

                lstAvailableApplication.SelectedIndex = 0;
                ApplicationManager.Instance.SaveApplications();
            }
        }

        private void MoveUpButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = (CommandInfo)lstAvailableActions.SelectedItem;
            int commandIndex = selected.Action.Commands.ToList().IndexOf(selected.Command);
            if (commandIndex > 0)
            {
                selected.Action.MoveCommand(commandIndex, commandIndex - 1);
                ApplicationManager.Instance.SaveApplications();
            }
        }

        private void MoveDownButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = (CommandInfo)lstAvailableActions.SelectedItem;
            int commandIndex = selected.Action.Commands.ToList().IndexOf(selected.Command);
            if (commandIndex + 1 < selected.Action.Commands.Count())
            {
                selected.Action.MoveCommand(commandIndex, commandIndex + 1);
                ApplicationManager.Instance.SaveApplications();
            }
        }

        private void ToggleAllActionsToggleSwitch_Click(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingToggleAllSwitch) return;

            try
            {
                var toggleSwitch = ((ToggleSwitch)sender);

                IApplication app = lstAvailableApplication.SelectedItem as IApplication;
                if (app == null) return;
                foreach (var action in app.Actions)
                {
                    action.IsEnabled = toggleSwitch.IsOn;
                }
                ApplicationManager.Instance.SaveApplications();
            }
            catch { }
        }

        private void ListBoxItem_OnMouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var listBoxItem = (ListBoxItem)sender;
            var listBox = UIHelper.GetParentDependencyObject<ListBox>(listBoxItem);
            if (ReferenceEquals(listBox, lstAvailableActions))
                Dispatcher.InvokeAsync(EditCommand, DispatcherPriority.Input);
            else if (ReferenceEquals(listBox, lstAvailableApplication))
                Dispatcher.InvokeAsync(EditApplication, DispatcherPriority.Input);
        }

        private void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            DownloadWindow DownloadWindow = new DownloadWindow();
            DownloadWindow.Show();
        }

        protected override void OnDrop(DragEventArgs e)
        {
            base.OnDrop(e);

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var newApps = new List<IApplication>();
                var newGestures = GestureManager.Instance.Gestures.ToList();
                try
                {
                    string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    foreach (var file in files)
                    {
                        switch (Path.GetExtension(file).ToLower())
                        {
                            case GestureSign.Common.Constants.ActionExtension:
                                var apps = FileManager.LoadObject<List<IApplication>>(file, false, true);
                                if (apps != null)
                                {
                                    newApps.AddRange(apps);
                                }
                                break;
                            case ".exe":
                                lstAvailableApplication.SelectedItem = ApplicationManager.Instance.AddApplication(new UserApp(), file);
                                break;
                            case ".lnk":
                                var targetPath = ShellLinkInterop.GetShortcutTarget(file);
                                if (!string.IsNullOrEmpty(targetPath) && Path.GetExtension(targetPath).ToLower() == ".exe")
                                {
                                    lstAvailableApplication.SelectedItem = ApplicationManager.Instance.AddApplication(new UserApp(), targetPath);
                                }
                                break;
                            case GestureSign.Common.Constants.ArchivesExtension:
                                {
                                    IEnumerable<IApplication> applications;
                                    IEnumerable<IGesture> gestures;
                                    Archive.LoadFromArchive(file, out applications, out gestures);

                                    if (applications != null)
                                        newApps.AddRange(applications);
                                    if (gestures != null)
                                    {
                                        foreach (var gesture in gestures)
                                        {
                                            if (newGestures.Find(g => g.Name == gesture.Name) == null)
                                                newGestures.Add(gesture);
                                        }
                                    }
                                    break;
                                }
                        }
                    }
                }
                catch (Exception exception)
                {
                    UIHelper.GetParentWindow(this).ShowModalMessageExternal(exception.GetType().Name, exception.Message);
                }
                if (newApps.Count != 0)
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        ExportImportDialog exportImportDialog = new ExportImportDialog(false, false, newApps, newGestures);
                        exportImportDialog.ShowDialog();
                    }, DispatcherPriority.Background);
                }
            }
            e.Handled = true;
        }

        #region Action Drag and Drop

        private void ActionButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _actionDragStartPoint = e.GetPosition(null);
            // Clear any previous drag state
            _draggedAction = null;
            _dropProcessed = false;
        }

        private void ActionButton_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            // Prevent new drag if one is already in progress
            if (e.LeftButton == MouseButtonState.Pressed && _draggedAction == null)
            {
                Point mousePos = e.GetPosition(null);
                Vector diff = _actionDragStartPoint - mousePos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    var button = sender as Button;
                    var groupItem = UIHelper.GetParentDependencyObject<GroupItem>(button);
                    var group = groupItem?.Content as CollectionViewGroup;
                    if (group == null || group.Items.Count == 0) return;

                    var firstCommand = group.Items[0] as CommandInfo;
                    _draggedAction = firstCommand?.Action;

                    if (_draggedAction != null)
                    {
                        _dropProcessed = false; // Reset flag for new drag operation
                        DataObject dragData = new DataObject("GestureAction", _draggedAction);
                        DragDrop.DoDragDrop(button, dragData, DragDropEffects.Move);
                        // DoDragDrop returns when user releases mouse
                        // DO NOT clear here - mouse might trigger new MouseMove events
                        // Will be cleared on next MouseDown
                    }
                }
            }
        }

        private void ActionButton_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("GestureAction"))
            {
                // Prevent duplicate Drop processing within single drag operation
                if (_dropProcessed)
                {
                    e.Handled = true;
                    return;
                }

                var sourceAction = e.Data.GetData("GestureAction") as IAction;

                // Check if this drag operation is valid
                if (_draggedAction == null || sourceAction != _draggedAction)
                {
                    e.Handled = true;
                    return;
                }

                var button = sender as Button;
                var groupItem = UIHelper.GetParentDependencyObject<GroupItem>(button);
                var group = groupItem?.Content as CollectionViewGroup;

                if (group != null && group.Items.Count > 0)
                {
                    var targetCommand = group.Items[0] as CommandInfo;
                    var targetAction = targetCommand?.Action;

                    if (sourceAction != null && targetAction != null && sourceAction != targetAction)
                    {
                        var app = lstAvailableApplication.SelectedItem as IApplication;
                        if (app != null)
                        {
                            var actions = app.Actions.ToList();
                            int sourceIndex = actions.IndexOf(sourceAction);
                            int targetIndex = actions.IndexOf(targetAction);

                            if (sourceIndex >= 0 && targetIndex >= 0 && sourceIndex != targetIndex)
                            {
                                // Mark as processed to prevent duplicate Drop events during UI refresh
                                _dropProcessed = true;

                                System.Diagnostics.Debug.WriteLine($"[AvailableActions] Moving action from {sourceIndex} to {targetIndex}");
                                app.MoveAction(sourceIndex, targetIndex);

                                ApplicationManager.Instance.SaveApplications();

                                // Note: RefreshCommandInfos removed - rely on ActionCollectionChanged Move event
                                // and Live Sorting to update the UI incrementally without full rebuild
                            }
                        }
                    }
                }

                e.Handled = true;
            }
        }

        private void ActionButton_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("GestureAction"))
            {
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        #endregion

        #region Command Drag and Drop

        private void CommandItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _commandDragStartPoint = e.GetPosition(null);
        }

        private void CommandItem_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                Point mousePos = e.GetPosition(null);
                Vector diff = _commandDragStartPoint - mousePos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    var listBoxItem = sender as ListBoxItem;
                    _draggedCommand = listBoxItem?.Content as CommandInfo;

                    if (_draggedCommand != null)
                    {
                        DataObject dragData = new DataObject("GestureCommand", _draggedCommand);
                        DragDrop.DoDragDrop(listBoxItem, dragData, DragDropEffects.Move);
                    }
                }
            }
        }

        private void CommandItem_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("GestureCommand"))
            {
                var sourceCommand = e.Data.GetData("GestureCommand") as CommandInfo;
                var listBoxItem = sender as ListBoxItem;
                var targetCommand = listBoxItem?.Content as CommandInfo;

                if (sourceCommand != null && targetCommand != null &&
                    sourceCommand != targetCommand &&
                    sourceCommand.Action == targetCommand.Action) // Only within same action
                {
                    var action = sourceCommand.Action;
                    var commands = action.Commands.ToList();

                    int sourceIndex = commands.IndexOf(sourceCommand.Command);
                    int targetIndex = commands.IndexOf(targetCommand.Command);

                    if (sourceIndex >= 0 && targetIndex >= 0)
                    {
                        // Use MoveCommand to trigger single Move event instead of Remove + Add
                        action.MoveCommand(sourceIndex, targetIndex);
                        ApplicationManager.Instance.SaveApplications();
                    }
                }

                e.Handled = true;
            }
        }

        private void CommandItem_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("GestureCommand"))
            {
                var sourceCommand = e.Data.GetData("GestureCommand") as CommandInfo;
                var listBoxItem = sender as ListBoxItem;
                var targetCommand = listBoxItem?.Content as CommandInfo;

                // Only allow drag within same action
                if (sourceCommand?.Action == targetCommand?.Action)
                {
                    e.Effects = DragDropEffects.Move;
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        #endregion

        #region Gesture Context Menu (Copy/Cut/Paste)

        private void CutGestureMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (SetGestureClipboard(sender))
                _cutActionSource = (IApplication)lstAvailableApplication.SelectedItem;
        }

        private void CopyGestureMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SetGestureClipboard(sender);
            _cutActionSource = null;
        }

        private void PasteGestureMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_commandClipboard.Count == 0) return;

            var menuItem = sender as MenuItem;
            var contextMenu = menuItem?.Parent as ContextMenu;
            var button = contextMenu?.PlacementTarget as Button;
            var groupItem = UIHelper.GetParentDependencyObject<GroupItem>(button);
            var group = groupItem?.Content as CollectionViewGroup;

            if (group == null || group.Items.Count == 0) return;

            var firstCommand = group.Items[0] as CommandInfo;
            var targetAction = firstCommand?.Action;
            var selectedApp = lstAvailableApplication.SelectedItem as IApplication;

            if (targetAction == null || selectedApp == null) return;

            foreach (var actionGroup in _commandClipboard.GroupBy(ci => ci.Action))
            {
                foreach (var info in actionGroup)
                {
                    if (_cutActionSource != null)
                    {
                        info.Action.RemoveCommand(info.Command);
                    }

                    var newCommand = ((Command)info.Command).Clone() as Command;
                    targetAction.AddCommand(newCommand);
                }
            }

            if (_cutActionSource != null)
            {
                _cutActionSource = null;
                _commandClipboard.Clear();
            }

            ApplicationManager.Instance.SaveApplications();
        }

        private bool SetGestureClipboard(object sender)
        {
            _commandClipboard.Clear();

            var menuItem = sender as MenuItem;
            var contextMenu = menuItem?.Parent as ContextMenu;
            var button = contextMenu?.PlacementTarget as Button;
            var groupItem = UIHelper.GetParentDependencyObject<GroupItem>(button);
            var group = groupItem?.Content as CollectionViewGroup;

            if (group == null) return false;

            foreach (CommandInfo commandInfo in group.Items)
            {
                if (commandInfo?.Command != null)
                    _commandClipboard.Add(commandInfo);
            }
            return _commandClipboard.Count != 0;
        }

        #endregion

        private void DeleteActionMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var contextMenu = menuItem?.Parent as ContextMenu;
            var button = contextMenu?.PlacementTarget as Button;
            var groupItem = UIHelper.GetParentDependencyObject<GroupItem>(button);
            var group = groupItem?.Content as CollectionViewGroup;

            if (group != null && group.Items.Count > 0)
            {
                var firstCommand = group.Items[0] as CommandInfo;
                var targetAction = firstCommand?.Action;
                var selectedApp = lstAvailableApplication.SelectedItem as IApplication;

                if (targetAction != null && selectedApp != null)
                {
                    selectedApp.RemoveAction(targetAction);
                    ApplicationManager.Instance.SaveApplications();
                }
            }
        }

        private void InsertCommandAboveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[AvailableActions] InsertCommandAboveMenuItem_Click called");
            var selectedCommand = lstAvailableActions.SelectedItem as CommandInfo;
            System.Diagnostics.Debug.WriteLine($"[AvailableActions] selectedCommand={(selectedCommand != null ? selectedCommand.CommandName : "null")}");

            if (selectedCommand == null) return;

            var newCommand = new Command();

            int commandIndex = selectedCommand.Action.Commands.ToList().IndexOf(selectedCommand.Command);
            System.Diagnostics.Debug.WriteLine($"[AvailableActions] Inserting command at index {commandIndex}");
            System.Diagnostics.Debug.WriteLine($"[AvailableActions] Commands before insert: {string.Join(", ", selectedCommand.Action.Commands.Select(c => c.PluginClass))}");

            if (commandIndex >= 0)
            {
                selectedCommand.Action.InsertCommand(commandIndex, newCommand);
                System.Diagnostics.Debug.WriteLine($"[AvailableActions] Commands after insert: {string.Join(", ", selectedCommand.Action.Commands.Select(c => c.PluginClass))}");
                ApplicationManager.Instance.SaveApplications();
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[AvailableActions] commandIndex < 0, command not found!");
            }
        }

        private void InsertCommandBelowMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var selectedCommand = lstAvailableActions.SelectedItem as CommandInfo;
            if (selectedCommand == null) return;

            var newCommand = new Command();

            int commandIndex = selectedCommand.Action.Commands.ToList().IndexOf(selectedCommand.Command);
            if (commandIndex >= 0)
            {
                selectedCommand.Action.InsertCommand(commandIndex + 1, newCommand);
                ApplicationManager.Instance.SaveApplications();
            }
        }

        #region Continuous Gesture Mode

        private bool _isUpdatingContinuousGestureMode;

        private void UpdateContinuousGestureModeComboBox(IApplication app)
        {
            if (app is IgnoredApp)
            {
                ContinuousGesturePanel.Visibility = Visibility.Collapsed;
                lstAvailableActions.Margin = new Thickness(0, 27, 0, 0);
                return;
            }

            ContinuousGesturePanel.Visibility = Visibility.Visible;
            lstAvailableActions.Margin = new Thickness(0, 57, 0, 0);

            _isUpdatingContinuousGestureMode = true;
            try
            {
                ContinuousGestureModeComboBox.Items.Clear();

                bool isGlobal = app is GlobalApp;

                if (!isGlobal)
                    ContinuousGestureModeComboBox.Items.Add(new ComboBoxItem
                    {
                        Content = LocalizationProvider.Instance.GetTextValue("ContinuousGesture.Inherit"),
                        Tag = ContinuousGestureMode.Inherit
                    });

                ContinuousGestureModeComboBox.Items.Add(new ComboBoxItem
                {
                    Content = LocalizationProvider.Instance.GetTextValue("ContinuousGesture.Scroll"),
                    Tag = ContinuousGestureMode.Scroll
                });
                ContinuousGestureModeComboBox.Items.Add(new ComboBoxItem
                {
                    Content = LocalizationProvider.Instance.GetTextValue("ContinuousGesture.Zoom"),
                    Tag = ContinuousGestureMode.Zoom
                });
                ContinuousGestureModeComboBox.Items.Add(new ComboBoxItem
                {
                    Content = LocalizationProvider.Instance.GetTextValue("ContinuousGesture.ScrollAndZoom"),
                    Tag = ContinuousGestureMode.ScrollAndZoom
                });
                ContinuousGestureModeComboBox.Items.Add(new ComboBoxItem
                {
                    Content = LocalizationProvider.Instance.GetTextValue("ContinuousGesture.None"),
                    Tag = ContinuousGestureMode.None
                });

                var currentMode = app.ContinuousGestureMode;
                // GlobalApp 不应为 Inherit，视为 Scroll
                if (isGlobal && currentMode == ContinuousGestureMode.Inherit)
                    currentMode = ContinuousGestureMode.Scroll;

                foreach (ComboBoxItem item in ContinuousGestureModeComboBox.Items)
                {
                    if ((ContinuousGestureMode)item.Tag == currentMode)
                    {
                        ContinuousGestureModeComboBox.SelectedItem = item;
                        break;
                    }
                }

                if (ContinuousGestureModeComboBox.SelectedItem == null)
                    ContinuousGestureModeComboBox.SelectedIndex = 0;
            }
            finally
            {
                _isUpdatingContinuousGestureMode = false;
            }
        }

        private void ContinuousGestureModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingContinuousGestureMode) return;

            var selectedApp = lstAvailableApplication.SelectedItem as IApplication;
            if (selectedApp == null) return;

            var selectedItem = ContinuousGestureModeComboBox.SelectedItem as ComboBoxItem;
            if (selectedItem == null) return;

            selectedApp.ContinuousGestureMode = (ContinuousGestureMode)selectedItem.Tag;
            ApplicationManager.Instance.SaveApplications();
        }

        private void ContinuousGestureConfigButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedApp = lstAvailableApplication.SelectedItem as IApplication;
            if (selectedApp == null) return;

            var dialog = new Dialogs.ContinuousGestureConfigDialog(selectedApp);
            if (dialog.ShowDialog() == true)
            {
                ApplicationManager.Instance.SaveApplications();
            }
        }

        #endregion

        #region Application Drag and Drop

        private Point _applicationDragStartPoint;
        private IApplication _draggedApplication;

        private void ApplicationItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _applicationDragStartPoint = e.GetPosition(null);
            _draggedApplication = null;
        }

        private void ApplicationItem_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _draggedApplication == null)
            {
                Point mousePos = e.GetPosition(null);
                Vector diff = _applicationDragStartPoint - mousePos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    var listBoxItem = sender as ListBoxItem;
                    _draggedApplication = listBoxItem?.Content as IApplication;

                    // 不允许拖动 GlobalApp
                    if (_draggedApplication != null && !(_draggedApplication is GlobalApp))
                    {
                        DataObject dragData = new DataObject("GestureApplication", _draggedApplication);
                        DragDrop.DoDragDrop(listBoxItem, dragData, DragDropEffects.Move);
                    }
                    else
                    {
                        _draggedApplication = null;
                    }
                }
            }
        }

        private void ApplicationItem_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("GestureApplication"))
            {
                var sourceApp = e.Data.GetData("GestureApplication") as IApplication;
                var listBoxItem = sender as ListBoxItem;
                var targetApp = listBoxItem?.Content as IApplication;

                // 不允许拖到 GlobalApp 上，但允许拖到其他位置
                if (sourceApp != null && targetApp != null &&
                    sourceApp != targetApp &&
                    !(sourceApp is GlobalApp))
                {
                    // 计算在 Applications 列表中的索引（不含 GlobalApp）
                    var apps = ApplicationManager.Instance.Applications;
                    int sourceIndex = apps.IndexOf(sourceApp);
                    int targetIndex = targetApp is GlobalApp ? 0 : apps.IndexOf(targetApp);

                    if (sourceIndex >= 0 && targetIndex >= 0 && sourceIndex != targetIndex)
                    {
                        ApplicationManager.Instance.MoveApplication(sourceIndex, targetIndex);
                        ApplicationManager.Instance.SaveApplications();
                    }
                }

                e.Handled = true;
            }
        }

        private void ApplicationItem_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("GestureApplication"))
            {
                var sourceApp = e.Data.GetData("GestureApplication") as IApplication;

                // 不允许拖动 GlobalApp
                if (sourceApp is GlobalApp)
                {
                    e.Effects = DragDropEffects.None;
                }
                else
                {
                    e.Effects = DragDropEffects.Move;
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        #endregion
    }
}
