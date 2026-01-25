using GestureSign.Common.Applications;
using GestureSign.ControlPanel.Common;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GestureSign.ControlPanel.ViewModel
{
    public class CommandInfoProvider
    {
        private IApplication _currentApp;
        private Task _addCommandTask;
        private ListBox _listBox;
        private volatile int _commandTaskCount;
        public ObservableCollection<CommandInfo> CommandInfos { get; } = new ObservableCollection<CommandInfo>();

        public CommandInfoProvider()
        {
        }

        public void RefreshCommandInfos(IApplication source, ListBox listBox)
        {
            _listBox = listBox;
            if (_currentApp != null)
            {
                _currentApp.CollectionChanged -= CommandCollectionChanged;
            }
            _currentApp = source;
            source.CollectionChanged -= ActionCollectionChanged;
            source.CollectionChanged += ActionCollectionChanged;

            Action<object> refreshAction = (o) =>
            {
                Application.Current.Dispatcher.Invoke(ClearCommandInfo, DispatcherPriority.Loaded);
                try
                {
                    var actionsList = source.Actions.ToList();

                    for (int actionIndex = 0; actionIndex < actionsList.Count; actionIndex++)
                    {
                        var currentAction = actionsList[actionIndex];
                        if (currentAction.Commands == null) continue;

                        currentAction.CollectionChanged -= CommandCollectionChanged;
                        currentAction.CollectionChanged += CommandCollectionChanged;

                        int commandIndexInAction = 0;
                        foreach (var info in currentAction.Commands.Select(c => CommandInfo.FromCommand(c, currentAction)))
                        {
                            if (_commandTaskCount > 1)
                                return;
                            try
                            {
                                // Set Order based on actionIndex and commandIndex
                                info.Order = actionIndex * 10000 + commandIndexInAction;
                                commandIndexInAction++;

                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    AddCommandInfo(info);
                                }, DispatcherPriority.Input);
                            }
                            catch { }
                        }
                    }
                }
                finally
                {
                    _commandTaskCount--;
                }
            };
            _addCommandTask = _addCommandTask?.ContinueWith(refreshAction) ?? Task.Factory.StartNew(refreshAction, null);
            _commandTaskCount++;
        }

        private void ClearCommandInfo()
        {
            foreach (var actionGroup in CommandInfos.GroupBy(ci => ci.Action))
            {
                actionGroup.Key.CollectionChanged -= CommandCollectionChanged;
            }
            CommandInfos.Clear();
        }

        private void CommandCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            var action = (IAction)sender;
            System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider.CommandCollectionChanged] ========== EVENT START ==========");
            System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider.CommandCollectionChanged] Event type: {e.Action}");
            System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider.CommandCollectionChanged] Action: {action.GestureName}");
            System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider.CommandCollectionChanged] OldItems count: {e.OldItems?.Count ?? 0}");
            System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider.CommandCollectionChanged] NewItems count: {e.NewItems?.Count ?? 0}");
            System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider.CommandCollectionChanged] CommandInfos count before: {CommandInfos.Count}");

            if (!_currentApp.Actions.Contains(action)) return;

            // Handle Move event separately - sync all Order values to avoid conflicts
            if (e.Action == NotifyCollectionChangedAction.Move)
            {
                var movedCommand = e.NewItems[0];
                var movedInfo = CommandInfos.FirstOrDefault(ci => ci.Command == movedCommand);

                System.Diagnostics.Debug.WriteLine(
                    $"[CommandInfoProvider.CommandCollectionChanged] Move event: {movedInfo?.Command?.Name} from {e.OldStartingIndex} to {e.NewStartingIndex}");

                // Critical: Must sync all commands' Order after move to avoid conflicts
                // Example: A(0), B(1), C(2) -> move C to 0 -> should be C(0), A(1), B(2)
                // Without sync, both C and A would have Order=0, causing incorrect sorting
                SyncOrderForAction(action);

                // Live sorting will handle visual update automatically - no Refresh needed
                _listBox.Dispatcher.InvokeAsync(() => _listBox.ScrollIntoView(_listBox.SelectedItem),
                    DispatcherPriority.Background);

                System.Diagnostics.Debug.WriteLine(
                    $"[CommandInfoProvider.CommandCollectionChanged] After Move sync: {string.Join(", ", CommandInfos.Where(ci => ci.Action == action).OrderBy(ci => ci.Order).Select(ci => $"{ci.Command.Name}(Order={ci.Order})"))}");
                System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider.CommandCollectionChanged] ========== EVENT END (Move) ==========");
                return; // Skip Add/Remove handling and avoid double sync
            }

            if (e.OldItems != null)
            {
                System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider.CommandCollectionChanged] Processing {e.OldItems.Count} removed items");
                foreach (var oldCommand in e.OldItems)
                {
                    var oldInfo = CommandInfos.FirstOrDefault(ci => ci.Command == oldCommand);
                    System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider.CommandCollectionChanged] Removing CommandInfo: {oldInfo?.Command?.Name} (Order={oldInfo?.Order})");
                    CommandInfos.Remove(oldInfo);
                    System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider.CommandCollectionChanged] CommandInfos count after remove: {CommandInfos.Count}");
                }
            }

            if (e.NewItems != null)
            {
                System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider.CommandCollectionChanged] Processing {e.NewItems.Count} new items");

                foreach (var newCommand in e.NewItems)
                {
                    var newInfo = CommandInfo.FromCommand((ICommand)newCommand, action);

                    // 设置 features 和 patternCount
                    string features;
                    int patternCount;
                    GestureItem gi = null;
                    if (newInfo.Action?.GestureName != null && GestureItemProvider.GestureMap.TryGetValue(newInfo.Action.GestureName, out gi))
                    {
                        features = gi.Features;
                        patternCount = gi.PatternCount;
                    }
                    else
                    {
                        features = string.Empty;
                        patternCount = 0;
                    }
                    newInfo.GestureFeatures = features;
                    newInfo.PatternCount = patternCount;

                    // 注意：这里不设置 Order，由 SyncOrderForAction 统一设置
                    System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider.CommandCollectionChanged] Adding CommandInfo: {newInfo.Command.Name}");

                    // 总是 Add 到末尾，让 ListCollectionView 自动排序
                    CommandInfos.Add(newInfo);
                    System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider.CommandCollectionChanged] CommandInfos count after add: {CommandInfos.Count}");
                    _listBox.SelectedItems.Add(newInfo);
                }
            }

            System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider.CommandCollectionChanged] Calling SyncOrderForAction...");
            // 关键：重新同步所有命令的 Order
            SyncOrderForAction(action);

            // Remove lcv.Refresh() - Live Sorting handles updates automatically
            // No manual refresh needed - property changes trigger automatic re-sort

            _listBox.Dispatcher.InvokeAsync(() => _listBox.ScrollIntoView(_listBox.SelectedItem), DispatcherPriority.Background);

            System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider.CommandCollectionChanged] After sync - Command order: {string.Join(", ", CommandInfos.Where(ci => ci.Action == action).OrderBy(ci => ci.Order).Select(ci => $"{ci.Command.Name}(Order={ci.Order})"))}");
            System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider.CommandCollectionChanged] CommandInfos count after: {CommandInfos.Count}");
            System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider.CommandCollectionChanged] ========== EVENT END ==========");
        }

        private void ActionCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider] ActionCollectionChanged: Action={e.Action}, OldItems={e.OldItems?.Count}, NewItems={e.NewItems?.Count}");
            var app = (IApplication)sender;
            if (app != _currentApp) return;

            // Handle Move event separately - resync all affected Actions' Order values
            if (e.Action == NotifyCollectionChangedAction.Move)
            {
                // When an Action moves, all Actions' indices change
                // Need to resync Order for all CommandInfos to reflect new Action positions
                SyncOrderForAllActions();

                // 异步刷新视图（非阻塞）
                // Live Sorting 在分组+排序场景下可能不响应，需要手动 Refresh
                _listBox.Dispatcher.InvokeAsync(() =>
                {
                    var lcv = _listBox.ItemsSource as System.Windows.Data.ListCollectionView;
                    lcv?.Refresh();
                }, System.Windows.Threading.DispatcherPriority.Normal);

                _listBox.Dispatcher.InvokeAsync(() => _listBox.ScrollIntoView(_listBox.SelectedItem), DispatcherPriority.Background);

                return; // Skip Add/Remove handling
            }

            if (e.OldItems != null)
            {
                System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider] Removing {e.OldItems.Count} old actions");
                foreach (IAction oldAction in e.OldItems)
                {
                    oldAction.CollectionChanged -= CommandCollectionChanged;
                    var oldInfos = CommandInfos.Where(ci => ci.Action == oldAction).ToList();
                    System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider] Removing action: {oldAction.GestureName}, {oldInfos.Count} commands");
                    foreach (var info in oldInfos)
                    {
                        CommandInfos.Remove(info);
                    }
                }
            }
            if (e.NewItems != null)
            {
                System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider] Adding {e.NewItems.Count} new actions");
                foreach (IAction newAction in e.NewItems)
                {
                    newAction.CollectionChanged += CommandCollectionChanged;

                    int actionIndex = app.Actions.ToList().IndexOf(newAction);
                    int orderBase = actionIndex * 10000;
                    int commandIndexInAction = 0;

                    System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider] Adding action: {newAction.GestureName}, actionIndex={actionIndex}");

                    foreach (ICommand newCommand in newAction.Commands)
                    {
                        var newInfo = CommandInfo.FromCommand(newCommand, newAction);

                        // 设置 features 和 patternCount
                        string features;
                        int patternCount;
                        GestureItem gi = null;
                        if (newInfo.Action?.GestureName != null && GestureItemProvider.GestureMap.TryGetValue(newInfo.Action.GestureName, out gi))
                        {
                            features = gi.Features;
                            patternCount = gi.PatternCount;
                        }
                        else
                        {
                            features = string.Empty;
                            patternCount = 0;
                        }
                        newInfo.GestureFeatures = features;
                        newInfo.PatternCount = patternCount;

                        // 设置初始 Order
                        newInfo.Order = orderBase + commandIndexInAction;
                        commandIndexInAction++;

                        // Add 到末尾，让 ListCollectionView 排序
                        CommandInfos.Add(newInfo);
                        System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider] Added command: {newInfo.Command.Name}, Order={newInfo.Order}");

                        _listBox.SelectedItems.Add(newInfo);
                    }
                }

                _listBox.Dispatcher.InvokeAsync(() => _listBox.ScrollIntoView(_listBox.SelectedItem), DispatcherPriority.Background);
            }
            System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider] ActionCollectionChanged completed, CommandInfos.Count={CommandInfos.Count}");
            System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider] Current order: {string.Join(", ", CommandInfos.Select(ci => ci.Action.GestureName).Distinct())}");
        }

        /// <summary>
        /// 同步指定 Action 中所有命令的 Order 值，确保与 action.Commands 的实际位置一致
        /// 使用 DeferRefresh 批量更新以避免每次 PropertyChanged 都触发重新排序
        /// </summary>
        private void SyncOrderForAction(IAction action)
        {
            System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider.SyncOrderForAction] ===== BEGIN SYNC =====");
            System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider.SyncOrderForAction] Action: {action.GestureName}");

            int actionIndex = _currentApp.Actions.ToList().IndexOf(action);
            if (actionIndex < 0)
            {
                System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider.SyncOrderForAction] Action not found in _currentApp.Actions, skipping");
                return; // 边界检查：action 已被移除
            }

            int orderBase = actionIndex * 10000;
            var allCommands = action.Commands.ToList();

            System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider.SyncOrderForAction] actionIndex={actionIndex}, orderBase={orderBase}, commands count={allCommands.Count}");
            System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider.SyncOrderForAction] Commands in action.Commands: {string.Join(", ", allCommands.Select(c => ((ICommand)c).Name))}");

            // 使用 DeferRefresh 批量更新 Order，避免每次 PropertyChanged 都触发 Live Sorting
            var lcv = System.Windows.Data.CollectionViewSource.GetDefaultView(CommandInfos) as System.Windows.Data.ListCollectionView;
            System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider.SyncOrderForAction] ListCollectionView obtained, starting DeferRefresh");

            using (lcv?.DeferRefresh())
            {
                for (int i = 0; i < allCommands.Count; i++)
                {
                    var cmdInfo = CommandInfos.FirstOrDefault(ci => ci.Command == allCommands[i]);
                    if (cmdInfo != null)
                    {
                        int oldOrder = cmdInfo.Order;
                        int newOrder = orderBase + i;
                        cmdInfo.Order = newOrder; // 触发 PropertyChanged，但 DeferRefresh 会推迟排序

                        if (oldOrder != newOrder)
                        {
                            System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider.SyncOrderForAction] Updated {cmdInfo.Command.Name}: Order {oldOrder} -> {newOrder}");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider.SyncOrderForAction] WARNING: CommandInfo not found for command at index {i}");
                    }
                }
            } // DeferRefresh Dispose 时一次性更新视图，避免多次重新排序

            System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider.SyncOrderForAction] DeferRefresh disposed, view should now update");
            System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider.SyncOrderForAction] Final Order values: {string.Join(", ", CommandInfos.Where(ci => ci.Action == action).OrderBy(ci => ci.Order).Select(ci => $"{ci.Command.Name}({ci.Order})"))}");
            System.Diagnostics.Debug.WriteLine($"[CommandInfoProvider.SyncOrderForAction] ===== END SYNC =====");
        }

        /// <summary>
        /// 重新同步所有 Action 的所有命令的 Order 值
        /// 用于 Action 拖动时，因为 Action 顺序改变会影响所有 Order 值
        /// 注意：调用者负责手动刷新视图（不使用DeferRefresh，避免与手动Refresh冲突）
        /// </summary>
        private void SyncOrderForAllActions()
        {
            var allActions = _currentApp.Actions.ToList();

            // 性能优化：建立 Command -> CommandInfo 的字典索引
            // 避免对每个 Command 执行 O(n) 的 FirstOrDefault 查找
            var commandToInfoMap = CommandInfos.ToDictionary(ci => ci.Command);

            for (int actionIndex = 0; actionIndex < allActions.Count; actionIndex++)
            {
                var action = allActions[actionIndex];
                int orderBase = actionIndex * 10000;
                var commands = action.Commands.ToList();

                for (int i = 0; i < commands.Count; i++)
                {
                    if (commandToInfoMap.TryGetValue(commands[i], out var cmdInfo))
                    {
                        int oldOrder = cmdInfo.Order;
                        int newOrder = orderBase + i;
                        if (oldOrder != newOrder)
                        {
                            cmdInfo.Order = newOrder;
                        }
                    }
                }
            }
        }

        private void AddCommandInfo(CommandInfo commandInfo)
        {
            string features;
            int patternCount;
            GestureItem gi = null;
            if (commandInfo.Action?.GestureName != null && GestureItemProvider.GestureMap.TryGetValue(commandInfo.Action.GestureName, out gi))
            {
                features = gi.Features;
                patternCount = gi.PatternCount;
            }
            else
            {
                features = string.Empty;
                patternCount = 0;
            }
            commandInfo.GestureFeatures = features;
            commandInfo.PatternCount = patternCount;
            // Note: Order should already be set by caller, don't override it here
            CommandInfos.Add(commandInfo);
        }
    }
}
