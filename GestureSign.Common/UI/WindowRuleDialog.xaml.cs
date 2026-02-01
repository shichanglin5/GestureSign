using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using GestureSign.Common.Applications;
using GestureSign.Common.Localization;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;

namespace GestureSign.Common.UI
{
    /// <summary>
    /// WindowRuleDialog.xaml 的交互逻辑
    /// 统一的窗口规则编辑对话框，供 ControlPanel 和 CorePlugins 使用
    /// </summary>
    public partial class WindowRuleDialog : MetroWindow
    {
        private WindowRule _windowRule;

        #region Dependency Properties

        public static readonly DependencyProperty ShowRuleNameProperty =
            DependencyProperty.Register("ShowRuleName", typeof(bool), typeof(WindowRuleDialog), new PropertyMetadata(true));

        public static readonly DependencyProperty ShowAddToPresetProperty =
            DependencyProperty.Register("ShowAddToPreset", typeof(bool), typeof(WindowRuleDialog), new PropertyMetadata(true));

        public static readonly DependencyProperty ShowArgumentsProperty =
            DependencyProperty.Register("ShowArguments", typeof(bool), typeof(WindowRuleDialog), new PropertyMetadata(false));

        /// <summary>
        /// 是否显示规则名称输入框
        /// </summary>
        public bool ShowRuleName
        {
            get { return (bool)GetValue(ShowRuleNameProperty); }
            set { SetValue(ShowRuleNameProperty, value); }
        }

        /// <summary>
        /// 是否显示"添加到预置"按钮
        /// </summary>
        public bool ShowAddToPreset
        {
            get { return (bool)GetValue(ShowAddToPresetProperty); }
            set { SetValue(ShowAddToPresetProperty, value); }
        }

        /// <summary>
        /// 是否显示应用程序参数输入框
        /// </summary>
        public bool ShowArguments
        {
            get { return (bool)GetValue(ShowArgumentsProperty); }
            set { SetValue(ShowArgumentsProperty, value); }
        }

        #endregion

        /// <summary>
        /// 获取编辑后的窗口规则
        /// </summary>
        public WindowRule WindowRule => _windowRule;

        /// <summary>
        /// 应用程序路径
        /// </summary>
        public string ApplicationPath
        {
            get => ConditionList.ApplicationPath;
            set => ConditionList.ApplicationPath = value;
        }

        /// <summary>
        /// 应用程序参数
        /// </summary>
        public string ApplicationArguments
        {
            get => ConditionList.ApplicationArguments;
            set => ConditionList.ApplicationArguments = value;
        }

        /// <summary>
        /// 匹配条件
        /// </summary>
        public List<MatchCondition> MatchConditions
        {
            get => ConditionList.GetConditions();
            set => ConditionList.SetConditions(value);
        }

        /// <summary>
        /// 创建新规则
        /// </summary>
        public WindowRuleDialog()
        {
            InitializeComponent();
            _windowRule = new WindowRule();
            Loaded += WindowRuleDialog_Loaded;
        }

        /// <summary>
        /// 编辑现有规则
        /// </summary>
        public WindowRuleDialog(WindowRule windowRule)
        {
            InitializeComponent();
            _windowRule = windowRule ?? new WindowRule();

            // 加载现有数据
            RuleNameTextBox.Text = _windowRule.Name ?? string.Empty;
            ConditionList.ApplicationPath = _windowRule.ApplicationPath ?? string.Empty;
            if (_windowRule.Conditions != null && _windowRule.Conditions.Count > 0)
            {
                ConditionList.SetConditions(_windowRule.Conditions);
            }

            Loaded += WindowRuleDialog_Loaded;
        }

        private void WindowRuleDialog_Loaded(object sender, RoutedEventArgs e)
        {
            // 同步 ShowArguments 到 ConditionList
            ConditionList.ShowArguments = ShowArguments;
        }

        /// <summary>
        /// 从窗口信息填充条件（会显示所有可用条件供用户选择）
        /// </summary>
        public void PopulateFromWindowInfo(WindowMatchInfo windowInfo)
        {
            ConditionList.PopulateFromWindowInfo(windowInfo);

            // Auto-fill rule name if empty and showing rule name
            if (ShowRuleName && string.IsNullOrEmpty(RuleNameTextBox.Text))
            {
                if (!string.IsNullOrEmpty(windowInfo.FileName))
                {
                    RuleNameTextBox.Text = System.IO.Path.GetFileNameWithoutExtension(windowInfo.FileName);
                }
            }
        }

        private void RuleCrosshair_Dragged(object sender, MouseButtonEventArgs e)
        {
            var dialog = new WindowSelectorDialog();

            if (Owner != null && Owner.IsLoaded)
            {
                dialog.Owner = Owner;
            }
            else if (IsLoaded)
            {
                dialog.Owner = this;
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            if (dialog.ShowDialog() == true && dialog.SelectedWindow != null)
            {
                var windowInfo = dialog.SelectedWindow;
                PopulateFromWindowInfo(windowInfo);
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var conditions = ConditionList.GetConditions();
            var applicationPath = ConditionList.ApplicationPath;

            // Validate: must have either conditions or applicationPath
            if ((conditions == null || conditions.Count == 0) && string.IsNullOrEmpty(applicationPath))
            {
                await this.ShowMessageAsync(
                    LocalizationProvider.Instance.GetTextValue("Common.Warning") ?? "Warning",
                    LocalizationProvider.Instance.GetTextValue("WindowRuleDialog.ValidationMessage")
                        ?? "Please add at least one match condition or specify an application path.");
                return;
            }

            // Update window rule
            _windowRule.Name = ShowRuleName ? RuleNameTextBox.Text.Trim() : null;
            _windowRule.ApplicationPath = applicationPath;
            _windowRule.Conditions = conditions ?? new List<MatchCondition>();

            DialogResult = true;
            Close();
        }

        private async void AddToPresetButton_Click(object sender, RoutedEventArgs e)
        {
            var conditions = ConditionList.GetConditions();
            var applicationPath = ConditionList.ApplicationPath;

            // Validate: must have either conditions or applicationPath
            if ((conditions == null || conditions.Count == 0) && string.IsNullOrEmpty(applicationPath))
            {
                await this.ShowMessageAsync(
                    LocalizationProvider.Instance.GetTextValue("Common.Warning") ?? "Warning",
                    LocalizationProvider.Instance.GetTextValue("WindowRuleDialog.ValidationMessage")
                        ?? "Please add at least one match condition or specify an application path.");
                return;
            }

            // 获取建议的名称
            var suggestedName = ShowRuleName ? RuleNameTextBox.Text.Trim() : string.Empty;
            if (string.IsNullOrEmpty(suggestedName) && conditions?.Count > 0)
            {
                suggestedName = conditions[0].Value;
            }

            // 使用 MahApps.Metro 的异步输入对话框
            var presetName = await this.ShowInputAsync(
                LocalizationProvider.Instance.GetTextValue("WindowPresets.SaveToPreset") ?? "Save to Preset",
                LocalizationProvider.Instance.GetTextValue("WindowPresets.EnterPresetName") ?? "Enter preset name:",
                new MetroDialogSettings { DefaultText = suggestedName });

            if (string.IsNullOrWhiteSpace(presetName))
                return;

            presetName = presetName.Trim();

            // 检查是否已存在
            var existing = WindowPresetManager.Instance.GetPresetByName(presetName);
            if (existing != null)
            {
                var confirmResult = await this.ShowMessageAsync(
                    LocalizationProvider.Instance.GetTextValue("Common.Confirm") ?? "Confirm",
                    string.Format(LocalizationProvider.Instance.GetTextValue("WindowPresets.OverwriteConfirm") ?? "Preset \"{0}\" already exists. Overwrite?", presetName),
                    MessageDialogStyle.AffirmativeAndNegative);

                if (confirmResult != MessageDialogResult.Affirmative)
                    return;

                WindowPresetManager.Instance.RemovePreset(presetName);
            }

            // 创建新的预置规则
            var preset = new WindowRule
            {
                Name = presetName,
                ApplicationPath = applicationPath,
                Conditions = conditions?.ToList() ?? new List<MatchCondition>()
            };

            WindowPresetManager.Instance.AddPreset(preset);
            WindowPresetManager.Instance.SavePresets();

            await this.ShowMessageAsync(
                LocalizationProvider.Instance.GetTextValue("Common.Success") ?? "Success",
                string.Format(LocalizationProvider.Instance.GetTextValue("WindowPresets.SavedSuccess") ?? "Preset \"{0}\" saved successfully.", presetName));
        }
    }
}
