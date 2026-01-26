using System;
using System.Windows;
using System.Windows.Controls;

namespace GestureSign.CorePlugins.InertialScroll
{
    /// <summary>
    /// InertialScrollUI.xaml 的交互逻辑
    /// </summary>
    public partial class InertialScrollUI : UserControl
    {
        private InertialScrollSettings _settings = new InertialScrollSettings();
        private bool _isLoading = false;  // 标记是否正在加载设置

        public InertialScrollSettings Settings
        {
            get => _settings;
            set
            {
                _isLoading = true;  // 加载期间禁用保存
                _settings = value ?? new InertialScrollSettings();
                LoadSettings();
                _isLoading = false;  // 加载完成后重新启用保存
            }
        }

        public InertialScrollUI()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 从设置加载到UI控件
        /// </summary>
        private void LoadSettings()
        {
            // 滚动方向
            foreach (ComboBoxItem item in DirectionComboBox.Items)
            {
                if (item.Tag.ToString() == _settings.Direction.ToString())
                {
                    DirectionComboBox.SelectedItem = item;
                    break;
                }
            }

            // 滚动精度和加速因子
            PixelsPerScrollUnitSlider.Value = _settings.PixelsPerScrollUnit;
            AccelerationSlider.Value = _settings.AccelerationFactor;

            // 反向
            ReverseCheckBox.IsChecked = _settings.ReverseDirection;
            ReverseHorizontalCheckBox.IsChecked = _settings.ReverseHorizontalDirection;

            // 抖动过滤阈值
            MinorAxisThresholdSlider.Value = _settings.MinorAxisThreshold;
        }

        /// <summary>
        /// 从UI控件保存到设置
        /// </summary>
        private void SaveSettings()
        {
            if (_isLoading) return;  // 加载期间跳过保存，避免触发循环更新

            // 滚动方向
            if (DirectionComboBox?.SelectedItem is ComboBoxItem selectedItem)
            {
                _settings.Direction = Enum.Parse<ScrollDirection>(selectedItem.Tag.ToString());
            }

            // 滚动精度和加速因子
            if (PixelsPerScrollUnitSlider != null)
                _settings.PixelsPerScrollUnit = PixelsPerScrollUnitSlider.Value;
            if (AccelerationSlider != null)
                _settings.AccelerationFactor = AccelerationSlider.Value;

            // 反向
            if (ReverseCheckBox != null)
                _settings.ReverseDirection = ReverseCheckBox.IsChecked ?? false;
            if (ReverseHorizontalCheckBox != null)
                _settings.ReverseHorizontalDirection = ReverseHorizontalCheckBox.IsChecked ?? false;

            // 抖动过滤阈值
            if (MinorAxisThresholdSlider != null)
                _settings.MinorAxisThreshold = MinorAxisThresholdSlider.Value;
        }

        #region Event Handlers

        private void DirectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SaveSettings();
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            SaveSettings();
        }

        private void ReverseCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            SaveSettings();
        }

        private void ReverseHorizontalCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            SaveSettings();
        }

        private void MinorAxisThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            SaveSettings();
        }

        #endregion
    }
}
