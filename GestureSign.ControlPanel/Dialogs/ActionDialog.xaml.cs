using System;
using System.Windows;
using System.Windows.Input;
using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using GestureSign.Common.Gestures;
using GestureSign.Common.Localization;
using GestureSign.Common.Log;
using GestureSign.ControlPanel.Common;
using GestureSign.ControlPanel.ViewModel;
using MahApps.Metro.Controls;
using System.Linq;
using GestureSign.Common.Input;

namespace GestureSign.ControlPanel.Dialogs
{
    /// <summary>
    /// Interaction logic for ActionDialog.xaml
    /// </summary>
    public partial class ActionDialog : MetroWindow
    {
        #region Private Instance Fields

        private readonly IAction _sourceAction;
        private IApplication _sourceApplication;

        #endregion

        #region Public Instance Fields

        public IAction NewAction { get; private set; } = new GestureSign.Common.Applications.Action();

        #endregion

        #region Dependency Properties

        public IGesture CurrentGesture
        {
            get { return (IGesture)GetValue(CurrentGestureProperty); }
            set { SetValue(CurrentGestureProperty, value); }
        }

        public static readonly DependencyProperty CurrentGestureProperty =
            DependencyProperty.Register(nameof(CurrentGesture), typeof(IGesture), typeof(ActionDialog), new PropertyMetadata(new Gesture()));

        public RecordedGestureDefinitionResult CurrentRecordedDefinition
        {
            get { return (RecordedGestureDefinitionResult)GetValue(CurrentRecordedDefinitionProperty); }
            set { SetValue(CurrentRecordedDefinitionProperty, value); }
        }

        public static readonly DependencyProperty CurrentRecordedDefinitionProperty =
            DependencyProperty.Register(nameof(CurrentRecordedDefinition), typeof(RecordedGestureDefinitionResult), typeof(ActionDialog), new PropertyMetadata(null, OnCurrentRecordedDefinitionChanged));

        private static void OnCurrentRecordedDefinitionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ActionDialog dialog && dialog.IsLoaded)
                dialog.LoadMatchStrategy();
        }

        #endregion

        #region Constructors

        protected ActionDialog()
        {
            InitializeComponent();
            GestureSelector.GetUIModifiers = GetSelectedModifiers;
        }

        public ActionDialog(IAction sourceAction, IApplication sourceApplication) : this()
        {
            _sourceAction = sourceAction;
            _sourceApplication = sourceApplication;
        }

        #endregion

        #region Events

        private void MetroWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_sourceAction != null)
            {
                ActionNameTextBox.Text = _sourceAction.Name;
                ActivateWindowCheckBox.IsChecked = _sourceAction.ActivateWindow;
                TouchScreenCheckBox.IsChecked = !_sourceAction.IgnoredDevices.HasFlag(Devices.TouchScreen);
                TouchPadCheckBox.IsChecked = !_sourceAction.IgnoredDevices.HasFlag(Devices.TouchPad);

                // Try to load contact gesture definition first (Tap/Click/TipTap)
                if (!string.IsNullOrEmpty(_sourceAction.GestureId))
                    CurrentRecordedDefinition ??= GetRecordedDefinitionById(_sourceAction.GestureId);

                if (CurrentRecordedDefinition != null)
                {
                    // Use factory to create display gesture with StrokeStyles
                    var displayGesture = ContactGestureDisplayFactory.CreateDisplayGesture(CurrentRecordedDefinition);
                    if (displayGesture != null)
                        CurrentGesture = displayGesture;
                }
                else
                {
                    var gesture = !string.IsNullOrEmpty(_sourceAction.GestureId)
                        ? GestureManager.Instance.GetGestureById(_sourceAction.GestureId)
                        : GestureManager.Instance.GetNewestGestureSample(_sourceAction.GestureName);
                    if (gesture != null)
                        CurrentGesture = gesture;
                }

            }

            // 加载手势匹配策略（仅轨迹手势）
            LoadMatchStrategy();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentRecordedDefinition != null && CurrentRecordedDefinition.Type != RecordedGestureType.Trajectory)
            {
                // Contact gesture (Tap/TipTap): save to ContactGestures only,
                // not to GestureManager (Gestures.json) to avoid StrokeStyles loss on reload
                var mods = GetSelectedModifiers();
                switch (CurrentRecordedDefinition.Type)
                {
                    case RecordedGestureType.Tap:
                        if (CurrentRecordedDefinition.TapGesture != null)
                            CurrentRecordedDefinition.TapGesture.Modifiers = mods;
                        break;
                    case RecordedGestureType.TipTap:
                        if (CurrentRecordedDefinition.TipTapGesture != null)
                            CurrentRecordedDefinition.TipTapGesture.Modifiers = mods;
                        break;
                }
                SaveRecordedDefinition(CurrentRecordedDefinition);
            }
            else if (CurrentGesture != null && CurrentGesture.PointPatterns != null)
            {
                // Trajectory gesture: save to GestureManager (Gestures.json)
                SaveGesture(CurrentGesture);
            }

            if (SaveAction())
            {
                if (!DialogResult.GetValueOrDefault())
                    DialogResult = true;
                Close();
            }
        }

        #endregion

        #region Private Methods

        private bool ShowErrorMessage(string title, string message)
        {
            MessageFlyoutText.Text = message;
            MessageFlyout.Header = title;
            MessageFlyout.IsOpen = true;
            return false;
        }

        private bool SaveAction()
        {
            // 直接使用 Contains 检查 _sourceAction 是否在列表中
            if (_sourceApplication.Actions.Contains(_sourceAction))
            {
                NewAction = _sourceAction;
            }
            else
            {
                _sourceApplication.AddAction(NewAction);
            }

            // Store new values
            NewAction.ActivateWindow = ActivateWindowCheckBox.IsChecked;
            NewAction.Name = ActionNameTextBox.Text.Trim();

            if (CurrentRecordedDefinition != null && CurrentRecordedDefinition.Type != RecordedGestureType.Trajectory)
            {
                NewAction.GestureId = CurrentRecordedDefinition.GestureId ?? string.Empty;
                NewAction.GestureName = CurrentRecordedDefinition.Name ?? string.Empty;
            }
            else
            {
                NewAction.GestureId = CurrentGesture?.Id ?? string.Empty;
                NewAction.GestureName = CurrentGesture?.Name ?? string.Empty;
            }

            Devices ignoredDevices = Devices.None;
            if (!TouchScreenCheckBox.IsChecked.GetValueOrDefault())
                ignoredDevices |= Devices.TouchScreen;
            if (!TouchPadCheckBox.IsChecked.GetValueOrDefault())
                ignoredDevices |= Devices.TouchPad;
            NewAction.IgnoredDevices = ignoredDevices;

            // Sync commands to ContactGestureConfig so runtime executor can find them
            SyncCommandsToContactGesture();

            ApplicationManager.Instance.SaveApplications();

            return true;
        }

        private RecordedGestureDefinitionResult GetRecordedDefinitionById(string gestureId)
        {
            if (string.IsNullOrEmpty(gestureId))
                return null;

            var global = ApplicationManager.Instance.GetGlobalApplication()?.ContactGestures;
            var tap = global?.Taps?.FirstOrDefault(t => t.Id == gestureId);
            if (tap != null)
            {
                return new RecordedGestureDefinitionResult
                {
                    Type = RecordedGestureType.Tap,
                    GestureId = tap.Id,
                    Name = tap.Name,
                    FingerCount = tap.FingerCount,
                    TapGesture = tap,
                };
            }

            var tipTap = global?.TipTaps?.FirstOrDefault(t => t.Id == gestureId);
            if (tipTap != null)
            {
                return new RecordedGestureDefinitionResult
                {
                    Type = RecordedGestureType.TipTap,
                    GestureId = tipTap.Id,
                    Name = tipTap.Name,
                    FingerCount = tipTap.FingerCount,
                    TipTapGesture = tipTap,
                };
            }

            return null;
        }

        private bool SaveRecordedDefinition(RecordedGestureDefinitionResult definition)
        {
            // 清理 GestureManager 中可能残留的同 Id 轨迹手势副本，避免运行时轨迹匹配误触发
            if (!string.IsNullOrEmpty(definition?.GestureId))
                GestureManager.Instance.DeleteGestureById(definition.GestureId);

            return ContactGestureDisplayFactory.SaveToGlobalApp(definition);
        }

        /// <summary>
        /// 把 Action.Commands 同步到对应的 ContactGestureConfig.Commands。
        /// 运行时执行器从 TapGestureConfig/ClickGestureConfig/TipTapGestureConfig.Commands 查找命令，
        /// 而 UI 把命令存到 Action.Commands，因此需要在保存时同步。
        /// </summary>
        private void SyncCommandsToContactGesture()
        {
            if (CurrentRecordedDefinition == null || NewAction?.Commands == null)
                return;

            string gestureId = CurrentRecordedDefinition.GestureId;
            if (string.IsNullOrEmpty(gestureId))
                return;

            var global = ApplicationManager.Instance.GetGlobalApplication();
            if (global?.ContactGestures == null)
                return;

            var commands = NewAction.Commands.OfType<Command>().ToList();

            switch (CurrentRecordedDefinition.Type)
            {
                case RecordedGestureType.Tap:
                    var tap = global.ContactGestures.Taps.FirstOrDefault(t => t.Id == gestureId);
                    if (tap != null)
                        tap.Commands = commands;
                    break;
                case RecordedGestureType.TipTap:
                    var tipTap = global.ContactGestures.TipTaps.FirstOrDefault(t => t.Id == gestureId);
                    if (tipTap != null)
                        tipTap.Commands = commands;
                    break;
            }
        }

        private bool SaveGesture(IGesture gesture)
        {
            if (string.IsNullOrEmpty(gesture.Id))
            {
                gesture.Id = GestureManager.Instance.GetNewGestureId(gesture.PointPatterns);
            }

            if (string.IsNullOrEmpty(gesture.Name))
            {
                gesture.Name = GestureManager.Instance.GetNewGestureName();
            }

            // 应用用户选择的匹配策略和修饰符
            gesture.MatchStrategy = GetSelectedMatchStrategy();
            gesture.Modifiers = GetSelectedModifiers();

            // 匹配到已有手势且不覆盖时，只更新策略，不覆盖轨迹数据
            bool isMatchedExisting = CurrentRecordedDefinition != null && CurrentRecordedDefinition.MatchedExistingDefinition;
            bool shouldOverwrite = GestureSelector.ShouldOverwriteExisting;
            if (isMatchedExisting && !shouldOverwrite)
            {
                var existingGesture = GestureManager.Instance.GetGestureById(gesture.Id);
                if (existingGesture != null)
                {
                    existingGesture.MatchStrategy = gesture.MatchStrategy;
                    existingGesture.Modifiers = gesture.Modifiers;
                    GestureManager.Instance.SaveGestures();
                }
                return true;
            }

            if (!string.IsNullOrEmpty(gesture.Id))
            {
                GestureManager.Instance.DeleteGestureById(gesture.Id);
            }

            GestureManager.Instance.AddGesture(gesture);
            GestureManager.Instance.SaveGestures();

            return true;
        }

        private void LoadMatchStrategy()
        {
            // 只对轨迹手势显示匹配策略选项
            bool isTrajectory = CurrentRecordedDefinition == null
                || CurrentRecordedDefinition.Type == RecordedGestureType.Trajectory;

            if (!isTrajectory)
            {
                MatchStrategyLabel.Visibility = Visibility.Collapsed;
                MatchStrategyComboBox.Visibility = Visibility.Collapsed;
            }
            else
            {
                MatchStrategyLabel.Visibility = Visibility.Visible;
                MatchStrategyComboBox.Visibility = Visibility.Visible;

                // 从已有手势读取策略
                var strategy = FingerMatchStrategy.Inherit;
                if (_sourceAction != null && !string.IsNullOrEmpty(_sourceAction.GestureId))
                {
                    var existingGesture = GestureManager.Instance.GetGestureById(_sourceAction.GestureId);
                    if (existingGesture != null)
                        strategy = existingGesture.MatchStrategy;
                }

                // 录制匹配到已有手势时，从匹配到的手势加载策略
                if (strategy == FingerMatchStrategy.Inherit
                    && CurrentRecordedDefinition?.MatchedExistingDefinition == true
                    && !string.IsNullOrEmpty(CurrentRecordedDefinition.GestureId))
                {
                    var matchedGesture = GestureManager.Instance.GetGestureById(CurrentRecordedDefinition.GestureId);
                    if (matchedGesture != null)
                        strategy = matchedGesture.MatchStrategy;
                }

                MatchStrategyComboBox.SelectedIndex = strategy switch
                {
                    FingerMatchStrategy.AllFingers => 1,
                    FingerMatchStrategy.FeatureFinger => 2,
                    _ => 0,
                };
            }

            // 修饰符面板：Tap/TipTap/轨迹均显示
            bool showModifiers;
            GestureModifiers initialModifiers = GestureModifiers.Default;
            // 修饰符面板已可见时，说明用户可能已手动调整过勾选状态，
            // 保留当前 UI 值而不用录制结果覆盖（录制结果的修饰符已由 GetUIModifiers 回调同步）
            bool preserveCurrentModifiers = ModifiersPanel.Visibility == Visibility.Visible;

            if (CurrentRecordedDefinition != null)
            {
                switch (CurrentRecordedDefinition.Type)
                {
                    case RecordedGestureType.Tap:
                        showModifiers = true;
                        if (!preserveCurrentModifiers)
                            initialModifiers = CurrentRecordedDefinition.TapGesture?.Modifiers ?? GestureModifiers.Default;
                        break;
                    case RecordedGestureType.TipTap:
                        showModifiers = true;
                        if (!preserveCurrentModifiers)
                            initialModifiers = CurrentRecordedDefinition.TipTapGesture?.Modifiers ?? GestureModifiers.Default;
                        break;
                    case RecordedGestureType.Trajectory:
                        showModifiers = true;
                        if (!preserveCurrentModifiers)
                            initialModifiers = CurrentGesture?.Modifiers ?? GestureModifiers.Default;
                        break;
                    default:
                        showModifiers = false;
                        break;
                }
            }
            else if (CurrentGesture != null)
            {
                // 轨迹手势（编辑已有手势）
                showModifiers = true;
                initialModifiers = CurrentGesture.Modifiers;
            }
            else
            {
                showModifiers = false;
            }

            var modifiersVisibility = showModifiers ? Visibility.Visible : Visibility.Collapsed;
            ModifiersLabel.Visibility = modifiersVisibility;
            ModifiersPanel.Visibility = modifiersVisibility;
            if (!preserveCurrentModifiers)
                InitModifiersCheckBoxes(initialModifiers);
        }

        private FingerMatchStrategy GetSelectedMatchStrategy()
        {
            return MatchStrategyComboBox.SelectedIndex switch
            {
                1 => FingerMatchStrategy.AllFingers,
                2 => FingerMatchStrategy.FeatureFinger,
                _ => FingerMatchStrategy.Inherit,
            };
        }

        private GestureModifiers GetSelectedModifiers()
        {
            var m = GestureModifiers.Default;
            if (ModPrimaryButton.IsChecked == true) m |= GestureModifiers.PrimaryButtonDown;
            if (ModCtrl.IsChecked == true) m |= GestureModifiers.Ctrl;
            if (ModShift.IsChecked == true) m |= GestureModifiers.Shift;
            if (ModAlt.IsChecked == true) m |= GestureModifiers.Alt;
            return m;
        }

        private void InitModifiersCheckBoxes(GestureModifiers modifiers)
        {
            ModPrimaryButton.IsChecked = modifiers.HasFlag(GestureModifiers.PrimaryButtonDown);
            ModCtrl.IsChecked = modifiers.HasFlag(GestureModifiers.Ctrl);
            ModShift.IsChecked = modifiers.HasFlag(GestureModifiers.Shift);
            ModAlt.IsChecked = modifiers.HasFlag(GestureModifiers.Alt);
        }

        private void MatchStrategyComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // UI 联动预留，当前不需要额外逻辑
        }

        #endregion
    }
}


