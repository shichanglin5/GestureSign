using GestureSign.Common.Applications;
using GestureSign.Common.Localization;
using MahApps.Metro.Controls;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace GestureSign.ControlPanel.Dialogs
{
    public partial class ContinuousGestureConfigDialog : MetroWindow
    {
        private readonly ContinuousGestureConfig _config;
        // 临时保存方向命令的编辑结果
        private readonly Dictionary<Gestures, List<Command>> _editingDirectionCommands;

        public ContinuousGestureConfigDialog(ContinuousGestureConfig config)
        {
            InitializeComponent();
            _config = config;

            // 深拷贝方向命令
            _editingDirectionCommands = new Dictionary<Gestures, List<Command>>();
            if (config.DirectionCommands != null)
            {
                foreach (var kv in config.DirectionCommands)
                {
                    _editingDirectionCommands[kv.Key] = kv.Value?.Select(c => (Command)((Command)c).Clone()).ToList()
                        ?? new List<Command>();
                }
            }

            InitializeUI();
        }

        private void InitializeUI()
        {
            // 缩放速度
            ZoomSpeedPanel.Visibility = _config.EnableZoom ? Visibility.Visible : Visibility.Collapsed;
            ZoomSpeedSlider.Value = _config.ZoomSpeed > 0 ? _config.ZoomSpeed : 1.0;

            // InertialScroll 设置
            ScrollSettingsPanel.Visibility = _config.ScrollMode == ContinuousScrollMode.InertialScroll
                ? Visibility.Visible : Visibility.Collapsed;

            var settings = _config.ScrollSettings ?? new InertialScrollSettings();

            // 滚动方向
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

            AxisActivationSlider.Value = settings.AxisActivationThreshold;
            PixelsPerScrollSlider.Value = settings.PixelsPerScrollUnit;
            AccelerationSlider.Value = settings.AccelerationFactor;
            ReverseDirectionCheckBox.IsChecked = settings.ReverseDirection;
            ReverseHorizontalCheckBox.IsChecked = settings.ReverseHorizontalDirection;
            WinUIDetectionCheckBox.IsChecked = settings.EnableWinUIDetection;

            // 惯性参数
            EnableMomentumCheckBox.IsChecked = settings.EnableMomentum;
            MomentumTimeConstantSlider.Value = settings.MomentumTimeConstantMs;
            MomentumMinVelocitySlider.Value = settings.MomentumMinVelocity;
            MomentumMaxDurationSlider.Value = settings.MomentumMaxDurationMs;
            MomentumDetailsPanel.Visibility = settings.EnableMomentum ? Visibility.Visible : Visibility.Collapsed;

            // Custom 模式方向命令
            CustomCommandsPanel.Visibility = _config.ScrollMode == ContinuousScrollMode.Custom
                ? Visibility.Visible : Visibility.Collapsed;

            if (_config.ScrollMode == ContinuousScrollMode.Custom)
            {
                BuildDirectionButtons();
            }
        }

        private void BuildDirectionButtons()
        {
            DirectionButtonsPanel.Children.Clear();

            var directions = new[]
            {
                (Gestures.Up, "ContinuousGesture.DirUp"),
                (Gestures.Down, "ContinuousGesture.DirDown"),
                (Gestures.Left, "ContinuousGesture.DirLeft"),
                (Gestures.Right, "ContinuousGesture.DirRight"),
            };

            foreach (var (direction, locKey) in directions)
            {
                var dirLabel = LocalizationProvider.Instance.GetTextValue(locKey);
                var hasCommands = _editingDirectionCommands.TryGetValue(direction, out var commands)
                    && commands != null && commands.Count > 0;

                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };

                var label = new TextBlock
                {
                    Text = dirLabel,
                    VerticalAlignment = VerticalAlignment.Center,
                    Width = 60,
                    FontSize = 13,
                };
                DockPanel.SetDock(label, Dock.Left);
                row.Children.Add(label);

                var commandText = hasCommands
                    ? string.Join(", ", commands.Select(c => c.PluginClass?.Split('.').LastOrDefault() ?? ""))
                    : LocalizationProvider.Instance.GetTextValue("ContinuousGesture.NotConfigured");

                var btn = new Button
                {
                    Content = commandText,
                    Tag = direction,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(8, 2, 8, 2),
                    FontSize = 12,
                };
                btn.Click += DirectionCommandButton_Click;
                row.Children.Add(btn);

                DirectionButtonsPanel.Children.Add(row);
            }
        }

        private void DirectionCommandButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;
            var direction = (Gestures)button.Tag;

            if (!_editingDirectionCommands.TryGetValue(direction, out var commands) || commands == null)
            {
                commands = new List<Command>();
                _editingDirectionCommands[direction] = commands;
            }

            // 如果没有命令，创建一个新的
            if (commands.Count == 0)
            {
                commands.Add(new Command());
            }

            // 使用第一个命令打开 CommandDialog
            var command = commands[0];
            var tempAction = new GestureSign.Common.Applications.Action();
            tempAction.AddCommand(command);

            var dialog = new CommandDialog(command, tempAction);
            if (dialog.ShowDialog() == true)
            {
                // CommandDialog 会直接修改 command 对象
                _editingDirectionCommands[direction] = new List<Command> { command };
                BuildDirectionButtons();
            }
            else if (string.IsNullOrEmpty(command.PluginClass))
            {
                // 用户取消且未配置，移除空命令
                commands.Clear();
            }
        }

        private void EnableMomentumCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (MomentumDetailsPanel != null)
                MomentumDetailsPanel.Visibility = EnableMomentumCheckBox.IsChecked == true
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // 保存缩放速度
            if (_config.EnableZoom)
                _config.ZoomSpeed = ZoomSpeedSlider.Value;

            // 保存 InertialScroll 设置
            if (_config.ScrollMode == ContinuousScrollMode.InertialScroll)
            {
                var settings = _config.ScrollSettings ?? new InertialScrollSettings();

                var selectedDirection = ScrollDirectionComboBox.SelectedItem as ComboBoxItem;
                if (selectedDirection != null)
                    settings.Direction = (ScrollDirection)selectedDirection.Tag;

                settings.AxisActivationThreshold = AxisActivationSlider.Value;
                settings.PixelsPerScrollUnit = PixelsPerScrollSlider.Value;
                settings.AccelerationFactor = AccelerationSlider.Value;
                settings.ReverseDirection = ReverseDirectionCheckBox.IsChecked == true;
                settings.ReverseHorizontalDirection = ReverseHorizontalCheckBox.IsChecked == true;
                settings.EnableWinUIDetection = WinUIDetectionCheckBox.IsChecked == true;

                settings.EnableMomentum = EnableMomentumCheckBox.IsChecked == true;
                settings.MomentumTimeConstantMs = MomentumTimeConstantSlider.Value;
                settings.MomentumMinVelocity = MomentumMinVelocitySlider.Value;
                settings.MomentumMaxDurationMs = MomentumMaxDurationSlider.Value;

                _config.ScrollSettings = settings;
            }

            // 保存 Custom 模式方向命令
            if (_config.ScrollMode == ContinuousScrollMode.Custom)
            {
                // 过滤掉空命令
                var cleaned = new Dictionary<Gestures, List<Command>>();
                foreach (var kv in _editingDirectionCommands)
                {
                    var validCommands = kv.Value?.Where(c => !string.IsNullOrEmpty(c.PluginClass)).ToList();
                    if (validCommands != null && validCommands.Count > 0)
                        cleaned[kv.Key] = validCommands;
                }
                _config.DirectionCommands = cleaned.Count > 0 ? cleaned : null;
            }

            DialogResult = true;
            Close();
        }
    }
}
