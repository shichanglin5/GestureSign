using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using GestureSign.Common.Localization;
using GestureSign.Common.UI;
using GestureSign.ControlPanel.Common;
using GestureSign.ControlPanel.UserControls;
using WindowRuleDialog = GestureSign.Common.UI.WindowRuleDialog;
using MahApps.Metro.Controls.Dialogs;
using ManagedWinapi.Windows;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using Point = System.Drawing.Point;

namespace GestureSign.ControlPanel.Dialogs
{
    /// <summary>
    /// Interaction logic for ApplicationDialog.xaml
    /// </summary>
    public partial class ApplicationDialog : TouchWindow
    {
        private bool _newApplication;
        private IApplication _currentApplication;
        private readonly ObservableCollection<IWindowRule> _priorityWindows = new ObservableCollection<IWindowRule>();
        private readonly ObservableCollection<IWindowRule> _matchRules = new ObservableCollection<IWindowRule>();

        public ApplicationListViewItem ApplicationListViewItem
        {
            get { return (ApplicationListViewItem)GetValue(ApplicationListViewItemProperty); }
            set { SetValue(ApplicationListViewItemProperty, value); }

        }
        public static readonly DependencyProperty ApplicationListViewItemProperty =
            DependencyProperty.Register("ApplicationListViewItem", typeof(ApplicationListViewItem), typeof(ApplicationDialog), new FrameworkPropertyMetadata(null));

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetCursorPos(out Point lpPoint);

        public ApplicationDialog()
        {
            InitializeComponent();
        }

        public ApplicationDialog(IApplication targetApplication, bool newApplication = false) : this()
        {
            _currentApplication = targetApplication;
            _newApplication = newApplication;
            switch (_currentApplication)
            {
                case UserApp app when _newApplication:
                    Title = LocalizationProvider.Instance.GetTextValue("ApplicationDialog.AddApplication");
                    break;
                case IgnoredApp app when _newApplication:
                    Title = LocalizationProvider.Instance.GetTextValue("ApplicationDialog.AddIgnoredAppTitle");
                    break;
                default:
                    Title = LocalizationProvider.Instance.GetTextValue("ApplicationDialog.EditApplication");
                    break;
            }
        }

        #region Events

        private void ApplicationDialog_OnLoaded(object sender, RoutedEventArgs e)
        {
            bool isUserApp = false;
            bool showLimitNumberOfFingers = false;
            switch (_currentApplication)
            {
                case UserApp userApp:
                    showLimitNumberOfFingers = true;
                    LimitNumberOfFingersSlider.Minimum = 1;
                    if (!_newApplication)
                    {
                        GroupComboBox.Text = _currentApplication.Group;
                        BlockTouchInputSlider.Value = userApp.BlockTouchInputThreshold;
                        LimitNumberOfFingersSlider.Value = userApp.LimitNumberOfFingers;
                    }
                    isUserApp = true;
                    break;
                case GlobalApp globalApp:
                    showLimitNumberOfFingers = true;
                    LimitNumberOfFingersSlider.Minimum = 2;
                    LimitNumberOfFingersSlider.Value = globalApp.LimitNumberOfFingers;
                    List<FrameworkElement> elements =
                        new List<FrameworkElement> { chCrosshair, ApplicationNameTextBox, ShowRunningButton, BrowseButton, MatchRulesPanel };
                    elements.ForEach(el => el.IsEnabled = false);
                    break;
            }
            if (!_newApplication)
            {
                ApplicationNameTextBox.Text = _currentApplication.Name;
                // 加载现有窗口规则
                InitializeMatchRules();
            }
            GroupComboBox.ItemsSource =
                ApplicationManager.Instance.Applications.Where(app => !string.IsNullOrEmpty(app.Group))
                    .Select(app => app.Group)
                    .Distinct()
                    .OrderBy(g => g);

            LimitNumberOfFingersSlider.Visibility = LimitNumberOfFingersInfoTextBlock.Visibility =
                LimitNumberOfFingersTextBlock.Visibility = showLimitNumberOfFingers ? Visibility.Visible : Visibility.Collapsed;
            GroupNameTextBlock.Visibility = GroupComboBox.Visibility =
                isUserApp ? Visibility.Visible : Visibility.Collapsed;

            BlockTouchInputSlider.Visibility = BlockTouchInputInfoTextBlock.Visibility = BlockTouchInputTextBlock.Visibility =
                    AppConfig.UiAccess && isUserApp ? Visibility.Visible : Visibility.Collapsed;

            // 初始化窗口规则列表
            MatchRulesListBox.ItemsSource = _matchRules;

            // 初始化优先级窗口设置
            InitializePriorityWindowsSettings();
        }

        private void InitializeMatchRules()
        {
            _matchRules.Clear();

            // 加载 MatchRules
            if (_currentApplication.MatchRules != null && _currentApplication.MatchRules.Count > 0)
            {
                foreach (var rule in _currentApplication.MatchRules)
                {
                    _matchRules.Add(rule);
                }
            }
        }

        private void InitializePriorityWindowsSettings()
        {
            // 只对 UserApp 和 GlobalApp 显示相关设置
            bool showSettings = _currentApplication is UserApp || _currentApplication is GlobalApp;
            MouseWindowDetectionTextBlock.Visibility = showSettings ? Visibility.Visible : Visibility.Collapsed;
            MouseWindowDetectionComboBox.Visibility = showSettings ? Visibility.Visible : Visibility.Collapsed;
            PriorityWindowsPanel.Visibility = showSettings ? Visibility.Visible : Visibility.Collapsed;

            if (!showSettings)
                return;

            // 初始化鼠标位置检测 ComboBox
            InitializeMouseWindowDetectionComboBox();

            // 加载优先级窗口列表
            _priorityWindows.Clear();
            if (_currentApplication.PriorityWindows != null)
            {
                foreach (var rule in _currentApplication.PriorityWindows)
                {
                    _priorityWindows.Add(rule);
                }
            }
            PriorityWindowsListBox.ItemsSource = _priorityWindows;
        }

        private void InitializeMouseWindowDetectionComboBox()
        {
            var items = new List<KeyValuePair<MouseWindowDetectionMode, string>>();

            // GlobalApp 不显示 Default 选项
            if (!(_currentApplication is GlobalApp))
            {
                items.Add(new KeyValuePair<MouseWindowDetectionMode, string>(
                    MouseWindowDetectionMode.Default,
                    LocalizationProvider.Instance.GetTextValue("Common.Default")));
            }

            items.Add(new KeyValuePair<MouseWindowDetectionMode, string>(
                MouseWindowDetectionMode.Enabled,
                LocalizationProvider.Instance.GetTextValue("Common.Enabled")));

            items.Add(new KeyValuePair<MouseWindowDetectionMode, string>(
                MouseWindowDetectionMode.Disabled,
                LocalizationProvider.Instance.GetTextValue("Common.Disabled")));

            MouseWindowDetectionComboBox.ItemsSource = items;
            MouseWindowDetectionComboBox.DisplayMemberPath = "Value";
            MouseWindowDetectionComboBox.SelectedValuePath = "Key";
            MouseWindowDetectionComboBox.SelectedValue = _currentApplication.MouseWindowDetection;
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofdExecutable = new OpenFileDialog
            {
                Filter = LocalizationProvider.Instance.GetTextValue("ApplicationDialog.ExecutableFile") + "|*.exe",
                InitialDirectory = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                    "Programs")
            };
            if (ofdExecutable.ShowDialog().Value)
            {
                ApplicationNameTextBox.Text = Path.GetFileNameWithoutExtension(ofdExecutable.FileName);

                // 创建窗口规则
                var rule = new WindowRule
                {
                    Name = Path.GetFileNameWithoutExtension(ofdExecutable.FileName),
                    ApplicationPath = ofdExecutable.FileName,
                    Conditions = new List<MatchCondition>
                    {
                        new MatchCondition
                        {
                            Type = MatchConditionType.ProcessName,
                            Value = ofdExecutable.SafeFileName
                        }
                    }
                };

                // 弹出编辑对话框
                var dialog = new WindowRuleDialog(rule) { Owner = this };
                if (dialog.ShowDialog() == true && dialog.WindowRule != null)
                {
                    _matchRules.Add(dialog.WindowRule);
                }
            }
        }

        private void ShowRunningButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new GestureSign.Common.UI.WindowSelectorDialog { Owner = this };
            if (dialog.ShowDialog() == true && dialog.SelectedWindow != null)
            {
                var info = dialog.SelectedWindow;
                AddMatchRuleFromWindowInfo(info);
            }
        }

        private void AddMatchRuleFromWindowInfo(WindowMatchInfo info)
        {
            // 更新应用名称（如果为空）
            if (string.IsNullOrWhiteSpace(ApplicationNameTextBox.Text))
            {
                ApplicationNameTextBox.Text = info.Title ?? info.FileName ?? string.Empty;
            }

            // 创建窗口规则
            var rule = new WindowRule
            {
                Name = Path.GetFileNameWithoutExtension(info.FileName ?? info.ProcessName),
                ApplicationPath = info.ProcessPath,
                Conditions = new List<MatchCondition>()
            };

            // 添加条件
            if (!string.IsNullOrEmpty(info.ClassName))
            {
                rule.Conditions.Add(new MatchCondition
                {
                    Type = MatchConditionType.ClassName,
                    Value = info.ClassName
                });
            }

            if (!string.IsNullOrEmpty(info.FileName))
            {
                rule.Conditions.Add(new MatchCondition
                {
                    Type = MatchConditionType.ProcessName,
                    Value = info.FileName
                });
            }

            // 弹出编辑对话框
            var ruleDialog = new WindowRuleDialog(rule) { Owner = this };
            if (ruleDialog.ShowDialog() == true && ruleDialog.WindowRule != null)
            {
                _matchRules.Add(ruleDialog.WindowRule);
            }

            // 同时更新 ApplicationListViewItem 供绑定使用
            ApplicationListViewItem = new ApplicationListViewItem
            {
                WindowClass = info.ClassName,
                WindowTitle = info.Title,
                WindowFilename = info.FileName,
                ApplicationIcon = info.Icon,
                ApplicationName = info.Title,
                AUMID = info.AUMID,
                ProcessPath = info.ProcessPath
            };
        }

        private void UpdateFromWindowInfo(WindowMatchInfo info)
        {
            // 向后兼容方法，调用新的 AddMatchRuleFromWindowInfo
            AddMatchRuleFromWindowInfo(info);
        }

        private void ChCrosshair_OnCrosshairDragging(object sender, MouseEventArgs e)
        {
            string className, title, fileName;
            var window = GetTargetWindow();
            var realWindow = ApplicationManager.GetWindowInfo(window, out className, out title, out fileName);
            try
            {
                // Set application name from filename
                ApplicationNameTextBox.Text = GetDescription(realWindow);
            }
            catch (Exception)
            {
                // ignored
            }
        }

        private void chCrosshair_CrosshairDragged(object sender, MouseButtonEventArgs e)
        {
            // 弹出窗口选择对话框
            var dialog = new GestureSign.Common.UI.WindowSelectorDialog { Owner = this };
            if (dialog.ShowDialog() == true && dialog.SelectedWindow != null)
            {
                var info = dialog.SelectedWindow;
                UpdateFromWindowInfo(info);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void DoneButton_Click(object sender, RoutedEventArgs e)
        {
            if (SaveApplication())
            {
                if (!DialogResult.GetValueOrDefault())
                    DialogResult = true;
                Close();
            }
        }

        private void BlockTouchInputSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            BlockTouchInputInfoTextBlock.Text = e.NewValue < 2
                ? LocalizationProvider.Instance.GetTextValue("Options.Off")
                : string.Format(LocalizationProvider.Instance.GetTextValue("ApplicationDialog.BlockTouchInputInfo"),
                    (int)e.NewValue);
        }

        private void LimitNumberOfFingersSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            LimitNumberOfFingersInfoTextBlock.Text = string.Format(
                LocalizationProvider.Instance.GetTextValue("ApplicationDialog.LimitNumberOfFingersInfo"),
                (int)e.NewValue);
        }

        private void AddPriorityWindow_Click(object sender, RoutedEventArgs e)
        {
            // 打开窗口规则编辑对话框
            var dialog = new WindowRuleDialog { Owner = this };
            if (dialog.ShowDialog() == true && dialog.WindowRule != null)
            {
                _priorityWindows.Add(dialog.WindowRule);
            }
        }

        private void RemovePriorityWindow_Click(object sender, RoutedEventArgs e)
        {
            if (PriorityWindowsListBox.SelectedItem is IWindowRule rule)
            {
                _priorityWindows.Remove(rule);
            }
        }

        private void EditPriorityWindow_Click(object sender, RoutedEventArgs e)
        {
            if (PriorityWindowsListBox.SelectedItem is WindowRule windowRule)
            {
                var dialog = new WindowRuleDialog(windowRule) { Owner = this };
                if (dialog.ShowDialog() == true)
                {
                    // 刷新列表显示
                    var index = PriorityWindowsListBox.SelectedIndex;
                    PriorityWindowsListBox.ItemsSource = null;
                    PriorityWindowsListBox.ItemsSource = _priorityWindows;
                    PriorityWindowsListBox.SelectedIndex = index;
                }
            }
            else if (PriorityWindowsListBox.SelectedItem is WindowRuleRef ruleRef)
            {
                // 编辑预置规则
                EditPresetRule(ruleRef.PresetId, () =>
                {
                    // 刷新列表显示
                    var index = PriorityWindowsListBox.SelectedIndex;
                    PriorityWindowsListBox.ItemsSource = null;
                    PriorityWindowsListBox.ItemsSource = _priorityWindows;
                    PriorityWindowsListBox.SelectedIndex = index;
                });
            }
        }

        private void ReferencePriorityWindowPreset_Click(object sender, RoutedEventArgs e)
        {
            if (WindowPresetManager.Instance.Presets.Count == 0)
            {
                this.ShowModalMessageExternal("提示", "没有可用的预置规则，请先在预置窗口规则页面创建");
                return;
            }

            var dialog = new SelectPresetDialog { Owner = this };
            if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.SelectedPresetId))
            {
                _priorityWindows.Add(new WindowRuleRef { PresetId = dialog.SelectedPresetId });
            }
        }

        private void PriorityWindowsListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var hasSelection = PriorityWindowsListBox.SelectedItem != null;
            EditPriorityWindowButton.IsEnabled = hasSelection;
            DeletePriorityWindowButton.IsEnabled = hasSelection;
        }

        private void PriorityWindowsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (PriorityWindowsListBox.SelectedItem != null)
            {
                EditPriorityWindow_Click(sender, e);
            }
        }

        private async void SavePriorityWindowToPreset_Click(object sender, RoutedEventArgs e)
        {
            if (PriorityWindowsListBox.SelectedItem is WindowRule windowRule)
            {
                await SaveWindowRuleToPreset(windowRule);
            }
            else if (PriorityWindowsListBox.SelectedItem is WindowRuleRef)
            {
                await this.ShowMessageAsync("提示", "该项已经是预置规则引用");
            }
        }

        private async System.Threading.Tasks.Task SaveWindowRuleToPreset(WindowRule rule)
        {
            // 弹出输入框获取预置名称
            var suggestedName = rule.Name ?? rule.GetDisplayName();
            var result = await this.ShowInputAsync(
                LocalizationProvider.Instance.GetTextValue("WindowPresets.SaveToPreset"),
                LocalizationProvider.Instance.GetTextValue("WindowPresets.EnterPresetName"),
                new MetroDialogSettings { DefaultText = suggestedName });

            if (string.IsNullOrWhiteSpace(result))
                return;

            // 检查是否已存在
            var existing = WindowPresetManager.Instance.GetPresetByName(result);
            if (existing != null)
            {
                var confirmResult = await this.ShowMessageAsync(
                    "确认",
                    $"预置规则 \"{result}\" 已存在，是否覆盖？",
                    MessageDialogStyle.AffirmativeAndNegative);

                if (confirmResult != MessageDialogResult.Affirmative)
                    return;

                WindowPresetManager.Instance.RemovePreset(result);
            }

            // 创建新的预置规则
            var preset = new WindowRule
            {
                Name = result,
                ApplicationPath = rule.ApplicationPath,
                Conditions = rule.Conditions?.ToList() ?? new List<MatchCondition>()
            };

            WindowPresetManager.Instance.AddPreset(preset);
            WindowPresetManager.Instance.SavePresets();
        }

        #region 窗口规则列表事件处理

        private void MatchRulesListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var hasSelection = MatchRulesListBox.SelectedItem != null;
            EditMatchRuleButton.IsEnabled = hasSelection;
            DeleteMatchRuleButton.IsEnabled = hasSelection;
        }

        private void MatchRulesListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (MatchRulesListBox.SelectedItem != null)
            {
                EditMatchRule_Click(sender, e);
            }
        }

        private void AddMatchRule_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new WindowRuleDialog { Owner = this };
            if (dialog.ShowDialog() == true && dialog.WindowRule != null)
            {
                _matchRules.Add(dialog.WindowRule);
            }
        }

        private void EditMatchRule_Click(object sender, RoutedEventArgs e)
        {
            if (MatchRulesListBox.SelectedItem is WindowRule windowRule)
            {
                var dialog = new WindowRuleDialog(windowRule) { Owner = this };
                if (dialog.ShowDialog() == true)
                {
                    // 刷新列表显示
                    var index = MatchRulesListBox.SelectedIndex;
                    MatchRulesListBox.ItemsSource = null;
                    MatchRulesListBox.ItemsSource = _matchRules;
                    MatchRulesListBox.SelectedIndex = index;
                }
            }
            else if (MatchRulesListBox.SelectedItem is WindowRuleRef ruleRef)
            {
                // 编辑预置规则
                EditPresetRule(ruleRef.PresetId, () =>
                {
                    // 刷新列表显示
                    var index = MatchRulesListBox.SelectedIndex;
                    MatchRulesListBox.ItemsSource = null;
                    MatchRulesListBox.ItemsSource = _matchRules;
                    MatchRulesListBox.SelectedIndex = index;
                });
            }
        }

        private void RemoveMatchRule_Click(object sender, RoutedEventArgs e)
        {
            if (MatchRulesListBox.SelectedItem is IWindowRule rule)
            {
                _matchRules.Remove(rule);
            }
        }

        private void ReferenceMatchRulePreset_Click(object sender, RoutedEventArgs e)
        {
            if (WindowPresetManager.Instance.Presets.Count == 0)
            {
                this.ShowModalMessageExternal("提示", "没有可用的预置规则，请先在预置窗口规则页面创建");
                return;
            }

            var dialog = new SelectPresetDialog { Owner = this };
            if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.SelectedPresetId))
            {
                _matchRules.Add(new WindowRuleRef { PresetId = dialog.SelectedPresetId });
            }
        }

        private async void SaveMatchRuleToPreset_Click(object sender, RoutedEventArgs e)
        {
            if (MatchRulesListBox.SelectedItem is WindowRule windowRule)
            {
                await SaveWindowRuleToPreset(windowRule);
            }
            else if (MatchRulesListBox.SelectedItem is WindowRuleRef)
            {
                await this.ShowMessageAsync("提示", "该项已经是预置规则引用");
            }
        }

        #endregion

        protected override void OnDrop(DragEventArgs e)
        {
            base.OnDrop(e);

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                try
                {
                    string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    if (files?.Length > 0)
                    {
                        string targetFile = files[0];
                        if (targetFile.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                        {
                            var resolvedTarget = ShellLinkInterop.GetShortcutTarget(targetFile);
                            if (string.IsNullOrEmpty(resolvedTarget))
                            {
                                // Unable to resolve shortcut, skip
                                return;
                            }
                            targetFile = resolvedTarget;
                        }
                        if (Path.GetExtension(targetFile).ToLower() == ".exe")
                        {
                            var versionInfo = FileVersionInfo.GetVersionInfo(targetFile);
                            ApplicationNameTextBox.Text = string.IsNullOrWhiteSpace(versionInfo.ProductName) ? Path.GetFileNameWithoutExtension(targetFile) : versionInfo.ProductName;

                            // 创建窗口规则
                            var rule = new WindowRule
                            {
                                Name = Path.GetFileNameWithoutExtension(targetFile),
                                ApplicationPath = targetFile,
                                Conditions = new List<MatchCondition>
                                {
                                    new MatchCondition
                                    {
                                        Type = MatchConditionType.ProcessName,
                                        Value = Path.GetFileName(targetFile)
                                    }
                                }
                            };

                            // 弹出编辑对话框
                            var dialog = new WindowRuleDialog(rule) { Owner = this };
                            if (dialog.ShowDialog() == true && dialog.WindowRule != null)
                            {
                                _matchRules.Add(dialog.WindowRule);
                            }
                        }
                    }
                }
                catch (Exception exception)
                {
                    this.ShowModalMessageExternal(exception.GetType().Name, exception.Message);
                }
            }
            e.Handled = true;
        }

        #endregion

        #region Private Methods

        private string GetDescription(SystemWindow window)
        {
            try
            {
                return window.Process.MainModule.FileVersionInfo.FileDescription;
            }
            catch (Exception)
            {
                return window.Title;
            }
        }

        private SystemWindow GetTargetWindow()
        {
            Point cursorPosition;
            GetCursorPos(out cursorPosition);

            SystemWindow window = SystemWindow.FromPointEx(cursorPosition.X, cursorPosition.Y, true, true);
            return window;
        }

        private bool ShowErrorMessage(string title, string message)
        {
            MessageFlyoutText.Text = message;
            MessageFlyout.Header = title;
            MessageFlyout.IsOpen = true;
            return false;
        }

        private bool SaveApplication()
        {
            if (_currentApplication is GlobalApp)
            {
                GlobalApp globalApp = (GlobalApp)_currentApplication;
                int newValue = (int)LimitNumberOfFingersSlider.Value;
                bool hasChanges = false;

                if (newValue != globalApp.LimitNumberOfFingers)
                {
                    globalApp.LimitNumberOfFingers = newValue;
                    hasChanges = true;
                }

                // 保存优先级窗口设置
                if (SavePriorityWindowsSettings(globalApp))
                {
                    hasChanges = true;
                }

                if (hasChanges)
                {
                    ApplicationManager.Instance.SaveApplications();
                }
                return true;
            }

            var matchRules = GetMatchRulesFromUI();

            if (matchRules.Count == 0)
            {
                return ShowErrorMessage(
                        LocalizationProvider.Instance.GetTextValue("ApplicationDialog.Messages.EmptyStringTitle"),
                        LocalizationProvider.Instance.GetTextValue("ApplicationDialog.Messages.EmptyConditions"));
            }

            string name = ApplicationNameTextBox.Text.Trim();
            switch (_currentApplication)
            {
                case UserApp userApp:
                    {
                        string groupName = string.IsNullOrWhiteSpace(GroupComboBox.Text) ? null : GroupComboBox.Text.Trim();

                        if (string.IsNullOrWhiteSpace(name))
                        {
                            return ShowErrorMessage(
                                LocalizationProvider.Instance.GetTextValue("ApplicationDialog.Messages.NoApplicationNameTitle"),
                                LocalizationProvider.Instance.GetTextValue("ApplicationDialog.Messages.NoApplicationName"));
                        }

                        if (_newApplication)
                        {
                            if (ApplicationManager.Instance.ApplicationExists(name))
                                return ShowErrorMessage(
                                        LocalizationProvider.Instance.GetTextValue("ApplicationDialog.Messages.AppExistsTitle"),
                                        LocalizationProvider.Instance.GetTextValue("ApplicationDialog.Messages.AppExists"));

                            var newApplication = new UserApp
                            {
                                BlockTouchInputThreshold = (int)BlockTouchInputSlider.Value,
                                LimitNumberOfFingers = (int)LimitNumberOfFingersSlider.Value,
                                Name = name,
                                Group = groupName,
                                MatchRules = matchRules,
                                MouseWindowDetection = (MouseWindowDetectionMode)MouseWindowDetectionComboBox.SelectedValue,
                                PriorityWindows = GetPriorityWindowsFromUI()
                            };
                            ApplicationManager.Instance.AddApplication(newApplication);
                        }
                        else
                        {
                            if (name != userApp.Name && ApplicationManager.Instance.ApplicationExists(name))
                            {
                                return ShowErrorMessage(
                                    LocalizationProvider.Instance.GetTextValue("ApplicationDialog.Messages.AppExistsTitle"),
                                    LocalizationProvider.Instance.GetTextValue("ApplicationDialog.Messages.AppExists"));
                            }

                            // 直接修改现有对象的属性，保持在列表中的位置不变
                            userApp.BlockTouchInputThreshold = (int)BlockTouchInputSlider.Value;
                            userApp.LimitNumberOfFingers = (int)LimitNumberOfFingersSlider.Value;
                            userApp.Name = name;
                            userApp.Group = groupName;
                            userApp.MatchRules = matchRules;
                            userApp.MouseWindowDetection = (MouseWindowDetectionMode)MouseWindowDetectionComboBox.SelectedValue;
                            userApp.PriorityWindows = GetPriorityWindowsFromUI();
                        }
                        break;
                    }
                case IgnoredApp ignoredApp:
                    {
                        // IgnoredApp 使用 MatchRules
                        if (string.IsNullOrEmpty(name))
                        {
                            // 从第一个规则获取名称
                            name = matchRules.FirstOrDefault()?.GetDisplayName() ?? "Unknown";
                        }

                        if (_newApplication)
                        {
                            ApplicationManager.Instance.AddApplication(new IgnoredApp(name, matchRules, true));
                        }
                        else
                        {
                            // 直接修改现有对象的属性，保持在列表中的位置不变
                            ignoredApp.Name = name;
                            ignoredApp.MatchRules = matchRules;
                        }
                        break;
                    }
            }
            ApplicationManager.Instance.SaveApplications();
            return true;
        }

        /// <summary>
        /// 从 UI 获取窗口规则配置
        /// </summary>
        private List<IWindowRule> GetMatchRulesFromUI()
        {
            return _matchRules.ToList();
        }

        /// <summary>
        /// 从 UI 获取优先级窗口配置
        /// </summary>
        private List<IWindowRule> GetPriorityWindowsFromUI()
        {
            // 直接返回 ObservableCollection 中的规则列表
            return _priorityWindows.ToList();
        }

        /// <summary>
        /// 保存优先级窗口设置到应用
        /// </summary>
        private bool SavePriorityWindowsSettings(IApplication app)
        {
            var mouseDetectionMode = (MouseWindowDetectionMode)MouseWindowDetectionComboBox.SelectedValue;
            var priorityWindows = GetPriorityWindowsFromUI();

            bool hasChanges = false;

            if (app.MouseWindowDetection != mouseDetectionMode)
            {
                app.MouseWindowDetection = mouseDetectionMode;
                hasChanges = true;
            }

            // 简单比较优先级窗口列表是否有变化
            if (!ArePriorityWindowsEqual(app.PriorityWindows, priorityWindows))
            {
                app.PriorityWindows = priorityWindows;
                hasChanges = true;
            }

            return hasChanges;
        }

        /// <summary>
        /// 比较两个优先级窗口列表是否相等
        /// </summary>
        private static bool ArePriorityWindowsEqual(List<IWindowRule> list1, List<IWindowRule> list2)
        {
            if (list1 == null && list2 == null) return true;
            if (list1 == null || list2 == null) return false;
            if (list1.Count != list2.Count) return false;

            for (int i = 0; i < list1.Count; i++)
            {
                // 简单比较显示名称，如果需要更精确的比较可以扩展
                if (list1[i].GetDisplayName() != list2[i].GetDisplayName())
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 编辑预置规则
        /// </summary>
        private void EditPresetRule(string presetId, System.Action onSaved = null)
        {
            var preset = WindowPresetManager.Instance.GetPresetById(presetId);
            if (preset == null)
            {
                this.ShowModalMessageExternal(
                    LocalizationProvider.Instance.GetTextValue("Common.Tip"),
                    LocalizationProvider.Instance.GetTextValue("ApplicationDialog.Messages.PresetNotFound"));
                return;
            }

            var dialog = new WindowRuleDialog(preset) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                // 保存预置规则
                WindowPresetManager.Instance.SavePresets();
                onSaved?.Invoke();
            }
        }

        #endregion
    }
}
