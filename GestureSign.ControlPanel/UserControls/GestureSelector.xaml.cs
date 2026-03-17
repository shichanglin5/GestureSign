using GestureSign.Common;
using GestureSign.Common.Gestures;
using GestureSign.Common.Input;
using GestureSign.Common.InterProcessCommunication;
using GestureSign.Common.Localization;
using GestureSign.Common.Log;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GestureSign.ControlPanel.Common;
using System;

namespace GestureSign.ControlPanel.UserControls
{
    public partial class GestureSelector : UserControl
    {
        private bool _isTrainingActive;
        private int _trainingRequestVersion;
        private string _diagnosticSessionFilePath;

        // 匹配到已有手势时的原始信息，用于在"已有"和"新建"之间切换
        private string _matchedOriginalId;
        private string _matchedOriginalName;
        private bool _isUsingExistingGesture;
        private IGesture _recordedGesture; // 用户录制的手势（覆盖模式下显示）

        private bool IsDiagnosticLoggingEnabled => (FindName("DiagnosticLoggingCheckBox") as CheckBox)?.IsChecked == true;

        private TextBlock DiagnosticFilePathText => FindName("DiagnosticFilePathTextBlock") as TextBlock;

        public IGesture CurrentGesture
        {
            get { return (IGesture)GetValue(CurrentGestureProperty); }
            set { SetValue(CurrentGestureProperty, value); }
        }

        public static readonly DependencyProperty CurrentGestureProperty =
            DependencyProperty.Register(nameof(CurrentGesture), typeof(IGesture), typeof(GestureSelector), new PropertyMetadata(new Gesture()));

        public RecordedGestureDefinitionResult CurrentRecordedDefinition
        {
            get { return (RecordedGestureDefinitionResult)GetValue(CurrentRecordedDefinitionProperty); }
            set { SetValue(CurrentRecordedDefinitionProperty, value); }
        }

        public static readonly DependencyProperty CurrentRecordedDefinitionProperty =
            DependencyProperty.Register(nameof(CurrentRecordedDefinition), typeof(RecordedGestureDefinitionResult), typeof(GestureSelector), new PropertyMetadata(null, CurrentRecordedDefinitionChanged));

        public IGesture OldGesture { get; set; }

        public bool ShouldOverwriteExisting => OverwriteExistingCheckBox.IsChecked == true;

        /// <summary>
        /// 从 ActionDialog 获取当前 UI 上勾选的修饰符，用于训练结果的重匹配。
        /// </summary>
        public Func<GestureModifiers> GetUIModifiers { get; set; }

        public GestureSelector()
        {
            InitializeComponent();
        }

        private static void CurrentRecordedDefinitionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
        }

        private void MessageProcessor_GotNewGestureDefinition(object sender, RecordedGestureDefinitionResult definition)
        {
            if (definition == null)
                return;

            // 用 UI 修饰符覆盖 Daemon 采样的修饰符，并重新匹配已有手势
            if (GetUIModifiers != null)
            {
                var uiModifiers = GetUIModifiers();
                if (definition.Type == RecordedGestureType.Trajectory && definition.TrajectoryGesture != null)
                {
                    definition.TrajectoryGesture.Modifiers = uiModifiers;
                    // 用 UI 修饰符重新匹配
                    var rematched = GestureManager.Instance.GetMostSimilarGestureName(
                        definition.TrajectoryGesture.PointPatterns, uiModifiers);
                    if (!string.IsNullOrEmpty(rematched))
                    {
                        var existing = GestureManager.Instance.GetNewestGestureSample(rematched) as Gesture;
                        if (existing != null)
                        {
                            definition.TrajectoryGesture.Id = existing.Id;
                            definition.TrajectoryGesture.Name = existing.Name;
                            definition.GestureId = existing.Id;
                            definition.Name = existing.Name;
                            definition.MatchedExistingDefinition = true;
                        }
                    }
                    else if (definition.MatchedExistingDefinition)
                    {
                        // Daemon 匹配到了（不区分修饰符），但 UI 修饰符下没有匹配 → 视为新手势
                        definition.MatchedExistingDefinition = false;
                        definition.TrajectoryGesture.Id = null;
                        definition.TrajectoryGesture.Name = null;
                        definition.GestureId = null;
                        definition.Name = null;
                    }
                }
                else if (definition.Type == RecordedGestureType.Tap && definition.TapGesture != null)
                {
                    definition.TapGesture.Modifiers = uiModifiers;
                }
                else if (definition.Type == RecordedGestureType.TipTap && definition.TipTapGesture != null)
                {
                    definition.TipTapGesture.Modifiers = uiModifiers;
                }
            }

            CurrentRecordedDefinition = definition;

            if (IsDiagnosticLoggingEnabled && !string.IsNullOrWhiteSpace(definition?.DiagnosticData))
            {
                try
                {
                    _diagnosticSessionFilePath ??= TrainingDiagnosticsSessionFileStore.CreateSessionFilePath();
                    _diagnosticSessionFilePath = TrainingDiagnosticsSessionFileStore.AppendRecording(_diagnosticSessionFilePath, definition.DiagnosticData);
                }
                catch (Exception exception)
                {
                    Logging.LogError($"[TrainingDiagnostics] Failed to write diagnostic file: {exception}");
                }
            }

            // 保存用户录制的手势
            if (definition?.Type == RecordedGestureType.Trajectory && definition.TrajectoryGesture != null)
                _recordedGesture = definition.TrajectoryGesture;
            else
                _recordedGesture = ContactGestureDisplayFactory.CreateDisplayGesture(definition);

            GestureTypeTextBlock.Text = ContactGestureText.GetRecordedGestureTypeText(definition) + GetModifierSuffix(definition);

            if (definition != null && definition.MatchedExistingDefinition)
            {
                _matchedOriginalId = definition.GestureId;
                _matchedOriginalName = definition.Name;
                _isUsingExistingGesture = true;

                // 默认显示已有手势的轨迹
                var existingGesture = GestureManager.Instance.GetGestureById(_matchedOriginalId);
                CurrentGesture = existingGesture ?? _recordedGesture;

                UpdateExistingTextBlock();
                ExistingTextBlock.Visibility = Visibility.Visible;
                // 不重置 OverwriteExistingCheckBox.IsChecked，保留用户的勾选状态
                OverwriteExistingCheckBox.Visibility = Visibility.Visible;

                // 如果用户已勾选覆盖，直接显示录制的手势
                if (OverwriteExistingCheckBox.IsChecked == true && _recordedGesture != null)
                    CurrentGesture = _recordedGesture;
            }
            else
            {
                _matchedOriginalId = null;
                _matchedOriginalName = null;
                _isUsingExistingGesture = false;
                _recordedGesture = null;
                CurrentGesture = definition?.Type == RecordedGestureType.Trajectory && definition?.TrajectoryGesture != null
                    ? definition.TrajectoryGesture
                    : ContactGestureDisplayFactory.CreateDisplayGesture(definition);
                ExistingTextBlock.Visibility = Visibility.Collapsed;
                OverwriteExistingCheckBox.Visibility = Visibility.Collapsed;
            }

            UpdateTrainingUi();
        }

        private async void SetTrainingState(bool state)
        {
            if (_isTrainingActive == state)
            {
                UpdateTrainingUi();
                return;
            }

            int requestVersion = ++_trainingRequestVersion;
            _isTrainingActive = state;

            if (state)
            {
                _diagnosticSessionFilePath = IsDiagnosticLoggingEnabled
                    ? TrainingDiagnosticsSessionFileStore.CreateSessionFilePath()
                    : null;
                MessageProcessor.GotNewGestureDefinition -= MessageProcessor_GotNewGestureDefinition;
                MessageProcessor.GotNewGestureDefinition += MessageProcessor_GotNewGestureDefinition;
                var started = await NamedPipe.SendMessageAsync(IpcCommands.StartTeaching, Constants.Daemon);
                if (requestVersion != _trainingRequestVersion || !_isTrainingActive)
                    return;

                if (!started)
                {
                    _isTrainingActive = false;
                    MessageProcessor.GotNewGestureDefinition -= MessageProcessor_GotNewGestureDefinition;
                    _diagnosticSessionFilePath = null;
                    Logging.LogWarning("[GestureSelector] Failed to start training because daemon is unavailable.");
                }
            }
            else
            {
                MessageProcessor.GotNewGestureDefinition -= MessageProcessor_GotNewGestureDefinition;
                _ = NamedPipe.SendMessageAsync(IpcCommands.StopTraining, Constants.Daemon);
                _diagnosticSessionFilePath = null;

                if (requestVersion != _trainingRequestVersion)
                    return;
            }

            UpdateTrainingUi();
        }

        private void UpdateTrainingUi()
        {
            RedrawButton.Visibility = Visibility.Visible;
            RedrawButton.Content = LocalizationProvider.Instance.GetTextValue(
                _isTrainingActive ? "GestureDefinition.Stop" : "GestureDefinition.Redraw");

            if (IsDiagnosticLoggingEnabled && !string.IsNullOrWhiteSpace(_diagnosticSessionFilePath))
            {
                if (DiagnosticFilePathText != null)
                {
                    DiagnosticFilePathText.Text = $"诊断文件: {_diagnosticSessionFilePath}";
                    DiagnosticFilePathText.Visibility = Visibility.Visible;
                }
            }
            else
            {
                if (DiagnosticFilePathText != null)
                {
                    DiagnosticFilePathText.Text = string.Empty;
                    DiagnosticFilePathText.Visibility = Visibility.Collapsed;
                }
            }

            if (_isTrainingActive)
            {
                DrawGestureTextBlock.Text = LocalizationProvider.Instance.GetTextValue("GestureDefinition.Recording");
                DrawGestureTextBlock.Visibility = Visibility.Visible;
                return;
            }

            DrawGestureTextBlock.Text = LocalizationProvider.Instance.GetTextValue("GestureDefinition.DrawGesture");
            DrawGestureTextBlock.Visibility = CurrentGesture == null && CurrentRecordedDefinition == null
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void RedrawButton_Click(object sender, RoutedEventArgs e)
        {
            SetTrainingState(!_isTrainingActive);
        }

        private void OverwriteExistingCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isUsingExistingGesture || string.IsNullOrEmpty(_matchedOriginalId))
                return;

            if (OverwriteExistingCheckBox.IsChecked == true)
            {
                // 勾选覆盖：显示用户录制的手势
                if (_recordedGesture != null)
                    CurrentGesture = _recordedGesture;
            }
            else
            {
                // 取消覆盖：恢复显示已有手势
                var existingGesture = GestureManager.Instance.GetGestureById(_matchedOriginalId);
                CurrentGesture = existingGesture ?? _recordedGesture;
            }
        }

        private void DiagnosticLoggingCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsDiagnosticLoggingEnabled)
            {
                _diagnosticSessionFilePath = null;
            }
            else if (_isTrainingActive && string.IsNullOrWhiteSpace(_diagnosticSessionFilePath))
            {
                _diagnosticSessionFilePath = TrainingDiagnosticsSessionFileStore.CreateSessionFilePath();
            }

            UpdateTrainingUi();
        }

        private void ExistingTextBlock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (CurrentRecordedDefinition == null || string.IsNullOrEmpty(_matchedOriginalId))
                return;

            if (_isUsingExistingGesture)
            {
                // 切换到新建模式：克隆手势避免修改已有手势的引用
                _isUsingExistingGesture = false;
                CurrentRecordedDefinition.MatchedExistingDefinition = false;

                if (CurrentRecordedDefinition.Type == RecordedGestureType.Trajectory && CurrentRecordedDefinition.TrajectoryGesture != null)
                {
                    var source = CurrentRecordedDefinition.TrajectoryGesture;
                    var cloned = new Gesture(null, source.PointPatterns, source.FingerCount)
                    {
                        MatchStrategy = source.MatchStrategy,
                        Modifiers = source.Modifiers,
                    };
                    CurrentRecordedDefinition.TrajectoryGesture = cloned;
                    CurrentGesture = cloned;
                    CurrentRecordedDefinition.GestureId = null;
                    CurrentRecordedDefinition.Name = null;
                }
                else
                {
                    CurrentRecordedDefinition.GestureId = Guid.NewGuid().ToString("N");
                    CurrentRecordedDefinition.Name = null;
                }
            }
            else
            {
                // 切换回已有手势
                _isUsingExistingGesture = true;
                CurrentRecordedDefinition.MatchedExistingDefinition = true;
                CurrentRecordedDefinition.GestureId = _matchedOriginalId;
                CurrentRecordedDefinition.Name = _matchedOriginalName;

                if (CurrentRecordedDefinition.Type == RecordedGestureType.Trajectory && CurrentRecordedDefinition.TrajectoryGesture != null)
                {
                    CurrentRecordedDefinition.TrajectoryGesture.Id = _matchedOriginalId;
                    CurrentRecordedDefinition.TrajectoryGesture.Name = _matchedOriginalName;
                }
            }

            UpdateExistingTextBlock();
            OverwriteExistingCheckBox.Visibility = _isUsingExistingGesture ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateExistingTextBlock()
        {
            if (_isUsingExistingGesture)
            {
                string suffix = GetModifierSuffix(CurrentRecordedDefinition);
                string name = !string.IsNullOrEmpty(_matchedOriginalName)
                    ? _matchedOriginalName
                    : _matchedOriginalId ?? (LocalizationProvider.Instance.GetTextValue("GestureDefinition.ExistingGestureNoName") ?? "Existing gesture");
                ExistingTextBlock.Text = name + suffix;
            }
            else
            {
                ExistingTextBlock.Text = LocalizationProvider.Instance.GetTextValue("GestureDefinition.NewGestureCreated") ?? "New gesture";
            }
        }

        private static string GetModifierSuffix(RecordedGestureDefinitionResult definition)
        {
            GestureModifiers modifiers = GestureModifiers.Default;
            if (definition?.Type == RecordedGestureType.Tap)
                modifiers = definition.TapGesture?.Modifiers ?? GestureModifiers.Default;
            else if (definition?.Type == RecordedGestureType.TipTap)
                modifiers = definition.TipTapGesture?.Modifiers ?? GestureModifiers.Default;

            if (modifiers == GestureModifiers.Default)
                return string.Empty;

            var parts = new System.Collections.Generic.List<string>();
            if (modifiers.HasFlag(GestureModifiers.PrimaryButtonDown))
                parts.Add(LocalizationProvider.Instance.GetTextValue("ActionDialog.ModPrimaryButton") ?? "Primary Button");
            if (modifiers.HasFlag(GestureModifiers.Ctrl)) parts.Add("Ctrl");
            if (modifiers.HasFlag(GestureModifiers.Shift)) parts.Add("Shift");
            if (modifiers.HasFlag(GestureModifiers.Alt)) parts.Add("Alt");
            return " + " + string.Join("+", parts);
        }

        private void imgGestureThumbnail_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (CurrentGesture?.PointPatterns == null && CurrentRecordedDefinition == null)
            {
                SetTrainingState(true);
                return;
            }

            // 已有手势打开时，显示类型标签
            if (CurrentRecordedDefinition != null)
            {
                GestureTypeTextBlock.Text = ContactGestureText.GetRecordedGestureTypeText(CurrentRecordedDefinition) + GetModifierSuffix(CurrentRecordedDefinition);
            }

            UpdateTrainingUi();
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            SetTrainingState(false);
        }
    }
}
