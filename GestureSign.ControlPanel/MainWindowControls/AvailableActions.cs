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

            CommandDialog commandDialog = new CommandDialog(selectedCommand, selectedAction);
            var result = commandDialog.ShowDialog();
            if (result != null && result.Value)
            {
                int index = selectedAction.Commands.ToList().IndexOf(selectedCommand);
                selectedAction.RemoveCommand(selectedCommand);
                selectedAction.InsertCommand(index, selectedCommand);
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
                var newCommand = new Command
                {
                    Name = LocalizationProvider.Instance.GetTextValue("Action.NewCommand")
                };
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

            var newCommand = new Command
            {
                Name = LocalizationProvider.Instance.GetTextValue("Action.NewCommand")
            };
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

            var newCommand = new Command
            {
                Name = LocalizationProvider.Instance.GetTextValue("Action.NewCommand")
            };
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
            ActionDialog actionDialog = new ActionDialog(sourceAction, selectedApplication);
            var result = actionDialog.ShowDialog();

            if (result != null && result.Value)
            {
                var newAction = actionDialog.NewAction;

                if (newAction != sourceAction)
                {
                    // Switching to different gesture: replace sourceAction position with newAction
                    lstAvailableActions.SelectedItem = null;

                    // Move commands from source to new action
                    foreach (CommandInfo info in infoList)
                    {
                        sourceAction.RemoveCommand(info.Command);
                        newAction.AddCommand(info.Command);
                    }

                    // Replace sourceAction with newAction at the same position
                    int sourceIndex = selectedApplication.Actions.ToList().IndexOf(sourceAction);
                    selectedApplication.RemoveAction(newAction); // Remove the one added by ActionDialog (at end)
                    selectedApplication.Insert(sourceIndex, newAction); // Insert at source position
                    selectedApplication.RemoveAction(sourceAction); // Remove old action
                }
                // else: Editing existing gesture, ActionDialog has already updated properties
                // No need to Remove/Insert, which would trigger unnecessary CollectionChanged events

                ApplicationManager.Instance.SaveApplications();
            }
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
                    newCommand.Name = ApplicationManager.GetNextCommandName(newCommand.Name, info.Action);
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
                    newCommand.Name = ApplicationManager.GetNextCommandName(newCommand.Name, info.Action);

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
                return;
            }

            var commandInfoProvider = ((ObjectDataProvider)Resources["CommandInfoProvider"]).ObjectInstance as CommandInfoProvider;
            if (commandInfoProvider == null) return;
            commandInfoProvider.RefreshCommandInfos(selectedApp, lstAvailableActions);

            // Keep two-level grouping (FingerCount -> Action)
            // Sort groups by FingerCount ascending, but don't sort actions within groups
            var lcv = lstAvailableActions.ItemsSource as ListCollectionView;
            if (lcv != null)
            {
                lcv.SortDescriptions.Clear();
                lcv.GroupDescriptions.Clear();
                lcv.SortDescriptions.Add(new SortDescription(nameof(CommandInfo.FingerCount), ListSortDirection.Ascending));
                lcv.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CommandInfo.FingerCount)));
                lcv.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CommandInfo.Action)));
                lcv.Refresh();
            }

            ToggleAllActionsToggleSwitch.IsEnabled = true;
            ToggleAllActionsToggleSwitch.IsOn = selectedApp.Actions.SelectMany(a => a.Commands).All(c => c.IsEnabled);

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
            try
            {
                var toggleSwitch = ((ToggleSwitch)sender);

                IApplication app = lstAvailableApplication.SelectedItem as IApplication;
                if (app == null) return;
                foreach (var command in app.Actions.SelectMany(a => a.Commands))
                {
                    command.IsEnabled = toggleSwitch.IsOn;
                }
                ApplicationManager.Instance.SaveApplications();

                foreach (CommandInfo ai in lstAvailableActions.Items)
                {
                    ai.IsEnabled = toggleSwitch.IsOn;
                }
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

                                // Drag semantics: Insert source BEFORE target
                                // List.Insert(index, item) inserts BEFORE the element at index
                                // After Remove(source):
                                //   - If dragging forward (source < target): target shifts left, use targetIndex
                                //   - If dragging backward (source > target): target stays, use targetIndex
                                // Result: Always use targetIndex!

                                app.RemoveAction(sourceAction);
                                var actionsAfterRemove = app.Actions.ToList();

                                // Always insert at target's position (source goes before target)
                                app.Insert(targetIndex, sourceAction);

                                var actionsAfterInsert = app.Actions.ToList();

                                ApplicationManager.Instance.SaveApplications();

                                // Refresh UI to ensure correct display order
                                // CollectionView sorting may be unstable after drag-drop
                                var commandInfoProvider = ((ObjectDataProvider)Resources["CommandInfoProvider"]).ObjectInstance as CommandInfoProvider;
                                if (commandInfoProvider != null)
                                {
                                    commandInfoProvider.RefreshCommandInfos(app, lstAvailableActions);
                                }
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
                        action.RemoveCommand(sourceCommand.Command);

                        // Log: After remove
                        var commandsAfterRemove = action.Commands.ToList();

                        // Insert source at target position
                        // No adjustment needed: targetIndex represents the desired final position
                        action.InsertCommand(targetIndex, sourceCommand.Command);

                        // Log: After insert
                        var commandsAfterInsert = action.Commands.ToList();
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

        #region Insert Above Menu Items

        private void InsertActionAboveMenuItem_Click(object sender, RoutedEventArgs e)
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
                    var newCommand = new Command
                    {
                        Name = LocalizationProvider.Instance.GetTextValue("Action.NewCommand")
                    };
                    var newAction = new GestureSign.Common.Applications.Action();
                    newAction.AddCommand(newCommand);

                    int targetIndex = selectedApp.Actions.ToList().IndexOf(targetAction);
                    if (targetIndex >= 0)
                    {
                        selectedApp.Insert(targetIndex, newAction);
                        ApplicationManager.Instance.SaveApplications();
                    }
                }
            }
        }

        private void InsertActionBelowMenuItem_Click(object sender, RoutedEventArgs e)
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
                    var newCommand = new Command
                    {
                        Name = LocalizationProvider.Instance.GetTextValue("Action.NewCommand")
                    };
                    var newAction = new GestureSign.Common.Applications.Action();
                    newAction.AddCommand(newCommand);

                    int targetIndex = selectedApp.Actions.ToList().IndexOf(targetAction);
                    if (targetIndex >= 0)
                    {
                        selectedApp.Insert(targetIndex + 1, newAction);
                        ApplicationManager.Instance.SaveApplications();
                    }
                }
            }
        }

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
            var selectedCommand = lstAvailableActions.SelectedItem as CommandInfo;
            if (selectedCommand == null) return;

            var newCommand = new Command
            {
                Name = LocalizationProvider.Instance.GetTextValue("Action.NewCommand")
            };

            int commandIndex = selectedCommand.Action.Commands.ToList().IndexOf(selectedCommand.Command);
            if (commandIndex >= 0)
            {
                selectedCommand.Action.InsertCommand(commandIndex, newCommand);
                ApplicationManager.Instance.SaveApplications();
            }
        }

        private void InsertCommandBelowMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var selectedCommand = lstAvailableActions.SelectedItem as CommandInfo;
            if (selectedCommand == null) return;

            var newCommand = new Command
            {
                Name = LocalizationProvider.Instance.GetTextValue("Action.NewCommand")
            };

            int commandIndex = selectedCommand.Action.Commands.ToList().IndexOf(selectedCommand.Command);
            if (commandIndex >= 0)
            {
                selectedCommand.Action.InsertCommand(commandIndex + 1, newCommand);
                ApplicationManager.Instance.SaveApplications();
            }
        }

        #endregion
    }
}
