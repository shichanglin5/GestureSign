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

            GestureTypeTextBlock.Text = ContactGestureText.GetRecordedGestureTypeText(definition);

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
                OverwriteExistingCheckBox.IsChecked = false;
                OverwriteExistingCheckBox.Visibility = Visibility.Visible;
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
                // 切换到新建模式
                _isUsingExistingGesture = false;
                CurrentRecordedDefinition.MatchedExistingDefinition = false;

                if (CurrentRecordedDefinition.Type == RecordedGestureType.Trajectory && CurrentRecordedDefinition.TrajectoryGesture != null)
                {
                    var gesture = CurrentRecordedDefinition.TrajectoryGesture;
                    gesture.Id = null;
                    gesture.Name = null;
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
                if (!string.IsNullOrEmpty(_matchedOriginalName))
                {
                    string template = LocalizationProvider.Instance.GetTextValue("GestureDefinition.ExistingGesture") ?? "Matched existing gesture: {0}";
                    ExistingTextBlock.Text = string.Format(template, _matchedOriginalName);
                }
                else
                {
                    ExistingTextBlock.Text = LocalizationProvider.Instance.GetTextValue("GestureDefinition.ExistingGestureNoName") ?? "Matched existing gesture";
                }
            }
            else
            {
                ExistingTextBlock.Text = LocalizationProvider.Instance.GetTextValue("GestureDefinition.NewGestureCreated") ?? "New gesture created";
            }
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
                GestureTypeTextBlock.Text = ContactGestureText.GetRecordedGestureTypeText(CurrentRecordedDefinition);
            }

            UpdateTrainingUi();
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            SetTrainingState(false);
        }
    }
}
