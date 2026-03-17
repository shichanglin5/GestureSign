using GestureSign.Common.Applications;
using GestureSign.Common.Localization;
using MahApps.Metro.Controls;
using System.Windows;
using System.Windows.Controls;

namespace GestureSign.ControlPanel.Dialogs
{
    public partial class ContinuousGestureConfigDialog : MetroWindow
    {
        private readonly ContinuousGestureConfig _config;

        public ContinuousGestureConfigDialog(ContinuousGestureConfig config)
        {
            InitializeComponent();
            _config = config;
            InitializeUI();
        }

        private void InitializeUI()
        {
            ZoomSpeedPanel.Visibility = _config.EnableZoom ? Visibility.Visible : Visibility.Collapsed;
            ZoomSpeedSlider.Value = _config.ZoomSpeed > 0 ? _config.ZoomSpeed : 1.0;
            ZoomSensitivitySlider.Value = _config.ZoomSensitivity > 0 ? _config.ZoomSensitivity : 1.0;

            ScrollSettingsPanel.Visibility = _config.ScrollMode == ContinuousScrollMode.InertialScroll
                ? Visibility.Visible : Visibility.Collapsed;

            var settings = _config.ScrollSettings ?? new InertialScrollSettings();

            ScrollDirectionComboBox.Items.Add(new ComboBoxItem
            {
                Content = LocalizationProvider.Instance.GetTextValue("ContinuousGesture.DirectionBoth"),
                Tag = ScrollDirection.Both
            });
            ScrollDirectionComboBox.Items.Add(new ComboBoxItem
            {
                Content = LocalizationProvider.Instance.GetTextValue("ContinuousGesture.DirectionVertical"),
                Tag = ScrollDirection.Vertical
            });
            ScrollDirectionComboBox.Items.Add(new ComboBoxItem
            {
                Content = LocalizationProvider.Instance.GetTextValue("ContinuousGesture.DirectionHorizontal"),
                Tag = ScrollDirection.Horizontal
            });

            foreach (ComboBoxItem item in ScrollDirectionComboBox.Items)
            {
                if ((ScrollDirection)item.Tag == settings.Direction)
                {
                    ScrollDirectionComboBox.SelectedItem = item;
                    break;
                }
            }
            if (ScrollDirectionComboBox.SelectedItem == null)
                ScrollDirectionComboBox.SelectedIndex = 0;

            NoiseRatioSlider.Value = settings.NoiseRatio;
            LockExitMinorDistanceSlider.Value = settings.LockExitMinorDistancePx;
            RelockRatioMultiplierSlider.Value = settings.RelockRatioMultiplier;
            PixelsPerScrollSlider.Value = settings.PixelsPerScrollUnit;
            AccelerationSlider.Value = settings.AccelerationFactor;
            ReverseDirectionCheckBox.IsChecked = settings.ReverseDirection;
            ReverseHorizontalCheckBox.IsChecked = settings.ReverseHorizontalDirection;
            WinUIDetectionCheckBox.IsChecked = settings.EnableWinUIDetection;

            MomentumTriggerMaxIdleSlider.Value = settings.MomentumTriggerMaxIdleMs;
            EnableMomentumCheckBox.IsChecked = settings.EnableMomentum;
            MomentumTimeConstantSlider.Value = settings.MomentumTimeConstantMs;
            MomentumMinVelocitySlider.Value = settings.MomentumMinVelocity;
            MomentumStopThresholdSlider.Value = settings.MomentumStopThreshold;
            MomentumMaxDurationSlider.Value = settings.MomentumMaxDurationMs;
            MomentumDetailsPanel.Visibility = settings.EnableMomentum ? Visibility.Visible : Visibility.Collapsed;

        }

        private void EnableMomentumCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (MomentumDetailsPanel != null)
            {
                MomentumDetailsPanel.Visibility = EnableMomentumCheckBox.IsChecked == true
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (_config.EnableZoom)
            {
                _config.ZoomSpeed = ZoomSpeedSlider.Value;
                _config.ZoomSensitivity = ZoomSensitivitySlider.Value;
            }

            if (_config.ScrollMode == ContinuousScrollMode.InertialScroll)
            {
                var settings = _config.ScrollSettings ?? new InertialScrollSettings();

                var selectedDirection = ScrollDirectionComboBox.SelectedItem as ComboBoxItem;
                if (selectedDirection != null)
                    settings.Direction = (ScrollDirection)selectedDirection.Tag;

                settings.NoiseRatio = NoiseRatioSlider.Value;
                settings.LockExitMinorDistancePx = LockExitMinorDistanceSlider.Value;
                settings.RelockRatioMultiplier = RelockRatioMultiplierSlider.Value;
                settings.PixelsPerScrollUnit = PixelsPerScrollSlider.Value;
                settings.AccelerationFactor = AccelerationSlider.Value;
                settings.ReverseDirection = ReverseDirectionCheckBox.IsChecked == true;
                settings.ReverseHorizontalDirection = ReverseHorizontalCheckBox.IsChecked == true;
                settings.EnableWinUIDetection = WinUIDetectionCheckBox.IsChecked == true;
                settings.MomentumTriggerMaxIdleMs = (int)MomentumTriggerMaxIdleSlider.Value;
                settings.EnableMomentum = EnableMomentumCheckBox.IsChecked == true;
                settings.MomentumTimeConstantMs = MomentumTimeConstantSlider.Value;
                settings.MomentumMinVelocity = MomentumMinVelocitySlider.Value;
                settings.MomentumStopThreshold = MomentumStopThresholdSlider.Value;
                settings.MomentumMaxDurationMs = MomentumMaxDurationSlider.Value;

                _config.ScrollSettings = settings;
            }

            _config.DirectionCommands = null;
            DialogResult = true;
            Close();
        }
    }
}

