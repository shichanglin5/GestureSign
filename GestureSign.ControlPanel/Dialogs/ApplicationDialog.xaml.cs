using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using GestureSign.Common.Localization;
using GestureSign.Common.UI;
using GestureSign.ControlPanel.Common;
using MahApps.Metro.Controls.Dialogs;
using ManagedWinapi.Windows;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
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
                        new List<FrameworkElement> { chCrosshair, ApplicationNameTextBox, ShowRunningButton, BrowseButton, matchConditionList };
                    elements.ForEach(el => el.IsEnabled = false);
                    break;
            }
            if (!_newApplication)
            {
                ApplicationNameTextBox.Text = _currentApplication.Name;
                // 加载现有条件
                matchConditionList.SetConditions(_currentApplication.MatchConditions);
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
                ApplicationNameTextBox.Text = System.IO.Path.GetFileNameWithoutExtension(ofdExecutable.FileName);

                // 使用新的条件列表方式
                var info = new WindowMatchInfo
                {
                    ProcessPath = ofdExecutable.FileName,
                    ProcessName = ofdExecutable.SafeFileName
                };
                matchConditionList.PopulateFromWindowInfo(info);
            }
        }

        private void ShowRunningButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new GestureSign.ControlPanel.Common.WindowSelectorDialog { Owner = this };
            if (dialog.ShowDialog() == true && dialog.SelectedWindow != null)
            {
                var info = dialog.SelectedWindow;
                UpdateFromWindowInfo(info);
            }
        }

        private void UpdateFromWindowInfo(WindowMatchInfo info)
        {
            // 更新应用名称
            ApplicationNameTextBox.Text = info.Title ?? info.FileName ?? string.Empty;

            // 使用条件列表控件填充
            matchConditionList.PopulateFromWindowInfo(info);

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
            var dialog = new GestureSign.ControlPanel.Common.WindowSelectorDialog { Owner = this };
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

                            // 使用新的条件列表方式
                            var info = new WindowMatchInfo
                            {
                                ProcessPath = targetFile,
                                ProcessName = Path.GetFileName(targetFile)
                            };
                            matchConditionList.PopulateFromWindowInfo(info);
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
                if (newValue != globalApp.LimitNumberOfFingers)
                {
                    globalApp.LimitNumberOfFingers = newValue;
                    ApplicationManager.Instance.SaveApplications();
                }
                return true;
            }

            var conditions = matchConditionList.GetConditions();

            if (conditions.Count == 0)
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

                        var newApplication = new UserApp
                        {
                            BlockTouchInputThreshold = (int)BlockTouchInputSlider.Value,
                            LimitNumberOfFingers = (int)LimitNumberOfFingersSlider.Value,
                            Name = name,
                            Group = groupName,
                            MatchConditions = conditions
                        };

                        if (_newApplication)
                        {
                            // 检查是否存在相同条件的应用
                            var sameMatchApplications = ApplicationManager.Instance.FindMatchApplications<UserApp>(conditions);
                            if (sameMatchApplications.Length != 0)
                            {
                                string sameApp = sameMatchApplications.Aggregate<IApplication, string>(null, (current, app) => current + (app.Name + " "));
                                return ShowErrorMessage(
                                    LocalizationProvider.Instance.GetTextValue("ApplicationDialog.Messages.StringConflictTitle"),
                                    string.Format(LocalizationProvider.Instance.GetTextValue("ApplicationDialog.Messages.ConditionConflict"), sameApp));
                            }

                            if (ApplicationManager.Instance.ApplicationExists(name))
                                return ShowErrorMessage(
                                        LocalizationProvider.Instance.GetTextValue("ApplicationDialog.Messages.AppExistsTitle"),
                                        LocalizationProvider.Instance.GetTextValue("ApplicationDialog.Messages.AppExists"));
                            ApplicationManager.Instance.AddApplication(newApplication);
                        }
                        else
                        {
                            var sameMatchApplications = ApplicationManager.Instance.FindMatchApplications<UserApp>(conditions, _currentApplication.Name);
                            if (sameMatchApplications.Length != 0)
                            {
                                string sameApp = sameMatchApplications.Aggregate<IApplication, string>(null, (current, app) => current + (app.Name + " "));
                                return ShowErrorMessage(
                                    LocalizationProvider.Instance.GetTextValue("ApplicationDialog.Messages.StringConflictTitle"),
                                    string.Format(LocalizationProvider.Instance.GetTextValue("ApplicationDialog.Messages.ConditionConflict"), sameApp));
                            }

                            if (name != _currentApplication.Name && ApplicationManager.Instance.ApplicationExists(name))
                            {
                                return ShowErrorMessage(
                                    LocalizationProvider.Instance.GetTextValue("ApplicationDialog.Messages.AppExistsTitle"),
                                    LocalizationProvider.Instance.GetTextValue("ApplicationDialog.Messages.AppExists"));
                            }

                            newApplication.Actions = _currentApplication.Actions;
                            ApplicationManager.Instance.ReplaceApplication(_currentApplication, newApplication);
                        }
                        break;
                    }
                case IgnoredApp ignoredApp:
                    {
                        if (string.IsNullOrEmpty(name))
                        {
                            // 从第一个条件获取名称
                            name = conditions.FirstOrDefault()?.Value ?? "Unknown";
                        }

                        if (!_newApplication)
                        {
                            var existingApp = ApplicationManager.Instance.FindMatchApplications<IgnoredApp>(conditions, _currentApplication.Name);
                            if (existingApp.Length != 0)
                            {
                                return ShowErrorMessage(
                                        LocalizationProvider.Instance.GetTextValue("ApplicationDialog.Messages.IgnoredAppExistsTitle"),
                                        LocalizationProvider.Instance.GetTextValue("ApplicationDialog.Messages.IgnoredAppExists"));
                            }
                            ApplicationManager.Instance.RemoveApplication(_currentApplication);
                        }
                        else
                        {
                            var existingApp = ApplicationManager.Instance.FindMatchApplications<IgnoredApp>(conditions);
                            if (existingApp.Length != 0)
                            {
                                return ShowErrorMessage(
                                    LocalizationProvider.Instance.GetTextValue("ApplicationDialog.Messages.IgnoredAppExistsTitle"),
                                    LocalizationProvider.Instance.GetTextValue("ApplicationDialog.Messages.IgnoredAppExists"));
                            }
                        }

                        ApplicationManager.Instance.AddApplication(new IgnoredApp(name, conditions, true));
                        break;
                    }
            }
            ApplicationManager.Instance.SaveApplications();
            return true;
        }

        #endregion
    }
}
