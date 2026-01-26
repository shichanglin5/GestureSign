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

            // 惯性设置
            EnableInertiaCheckBox.IsChecked = _settings.EnableInertia;
            InertiaStrengthSlider.Value = _settings.InertiaStrength;
            DurationSlider.Value = _settings.InertiaDuration;
            DecayRateSlider.Value = _settings.DecayRate;

            // 距离倍数
            MultiplierSlider.Value = _settings.DistanceMultiplier;

            // 最小速度和像素转换比例
            MinVelocityTextBox.Text = _settings.MinimumVelocity.ToString();
            PixelsPerScrollUnitSlider.Value = _settings.PixelsPerScrollUnit;

            // 反向
            ReverseCheckBox.IsChecked = _settings.ReverseDirection;
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

            // 惯性设置
            if (EnableInertiaCheckBox != null)
                _settings.EnableInertia = EnableInertiaCheckBox.IsChecked ?? true;
            if (InertiaStrengthSlider != null)
                _settings.InertiaStrength = InertiaStrengthSlider.Value;
            if (DurationSlider != null)
                _settings.InertiaDuration = DurationSlider.Value;
            if (DecayRateSlider != null)
                _settings.DecayRate = DecayRateSlider.Value;

            // 距离倍数
            if (MultiplierSlider != null)
                _settings.DistanceMultiplier = MultiplierSlider.Value;

            // 最小速度和像素转换比例
            if (MinVelocityTextBox != null && double.TryParse(MinVelocityTextBox.Text, out double minVel))
            {
                _settings.MinimumVelocity = minVel;
            }
            if (PixelsPerScrollUnitSlider != null)
                _settings.PixelsPerScrollUnit = PixelsPerScrollUnitSlider.Value;

            // 反向
            if (ReverseCheckBox != null)
                _settings.ReverseDirection = ReverseCheckBox.IsChecked ?? false;
        }

        #region Event Handlers

        private void DirectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SaveSettings();
        }

        private void EnableInertiaCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            SaveSettings();
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            SaveSettings();
        }

        private void MinVelocityTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SaveSettings();
        }

        private void ReverseCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            SaveSettings();
        }

        #endregion
    }
}
