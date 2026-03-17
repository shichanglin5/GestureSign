using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GestureSign.ControlPanel.Common
{
    /// <summary>
    /// http://matthamilton.net/touchscrolling-for-scrollviewer
    /// </summary>
    public class DragScrolling : DependencyObject
    {
        private static double _verticalOffset;
        private static Point _downPoint;

        public static bool GetIsEnabled(DependencyObject obj)
        {
            return (bool)obj.GetValue(IsEnabledProperty);
        }

        public static void SetIsEnabled(DependencyObject obj, bool value)
        {
            obj.SetValue(IsEnabledProperty, value);
        }

        public bool IsEnabled
        {
            get { return (bool)GetValue(IsEnabledProperty); }
            set { SetValue(IsEnabledProperty, value); }
        }

        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(DragScrolling), new UIPropertyMetadata(false, IsEnabledChanged));

        private static void IsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var target = d as ScrollViewer;
            if (target == null) return;

            if ((bool)e.NewValue)
            {
                UnregisterEvents(target);
                RegisterEvents(target);
            }
        }

        static void RegisterEvents(FrameworkElement target)
        {
            target.PreviewMouseLeftButtonDown += target_PreviewMouseLeftButtonDown;
            target.PreviewMouseMove += target_PreviewMouseMove;
            target.PreviewMouseLeftButtonUp += target_PreviewMouseLeftButtonUp;
        }

        static void UnregisterEvents(FrameworkElement target)
        {
            target.PreviewMouseLeftButtonDown -= target_PreviewMouseLeftButtonDown;
            target.PreviewMouseMove -= target_PreviewMouseMove;
            target.PreviewMouseLeftButtonUp -= target_PreviewMouseLeftButtonUp;
        }

        static void target_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var target = sender as ScrollViewer;
            if (target == null) return;

            // 不捕获来自交互控件的点击，让它们正常处理
            if (e.OriginalSource is DependencyObject source)
            {
                if (IsInteractiveControl(source))
                {
                    return;
                }
            }

            _verticalOffset = target.VerticalOffset;
            _downPoint = e.GetPosition(target);
            target.CaptureMouse();
        }

        /// <summary>
        /// 检查元素或其父元素是否是交互控件
        /// </summary>
        private static bool IsInteractiveControl(DependencyObject element)
        {
            while (element != null)
            {
                if (element is System.Windows.Controls.Primitives.ButtonBase ||
                    element is System.Windows.Controls.Primitives.ToggleButton ||
                    element is TextBox ||
                    element is ComboBox ||
                    element is ComboBoxItem ||
                    element is Slider)
                {
                    return true;
                }
                element = System.Windows.Media.VisualTreeHelper.GetParent(element);
            }
            return false;
        }

        static void target_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var target = sender as ScrollViewer;
            if (target == null || !target.IsMouseCaptured) return;

            if (Math.Abs(e.GetPosition(target).Y - _downPoint.Y) > 20)
            {
                e.Handled = true;
            }
            target.ReleaseMouseCapture();
        }

        static void target_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var target = sender as ScrollViewer;
            if (target == null || !target.IsMouseCaptured) return;

            var point = e.GetPosition(target);

            var dy = point.Y - _downPoint.Y;
            target.ScrollToVerticalOffset(_verticalOffset - dy);
        }
    }
}
