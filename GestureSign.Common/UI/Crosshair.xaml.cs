using System;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;

namespace GestureSign.Common.UI
{
    /// <summary>
    /// Crosshair.xaml 的交互逻辑 - 用于窗口捕获的十字准星控件
    /// </summary>
    public partial class Crosshair : UserControl
    {
        public event EventHandler<MouseButtonEventArgs> CrosshairDragged;
        public event EventHandler<MouseEventArgs> CrosshairDragging;

        private bool _isMove = false;

        public Crosshair()
        {
            InitializeComponent();
        }

        private void Crosshair_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as Ellipse;
            if (source != null)
            {
                _isMove = true;
                Dragger.Visibility = System.Windows.Visibility.Hidden;
                source.Cursor = Cursors.Cross;
                source.CaptureMouse();
            }
        }

        private void Crosshair_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.OriginalSource is Ellipse && _isMove)
            {
                CrosshairDragging?.Invoke(this, e);
            }
        }

        private void Crosshair_MouseUp(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as Ellipse;
            if (source != null)
            {
                source.ReleaseMouseCapture();
                source.Cursor = null;
                Dragger.Visibility = System.Windows.Visibility.Visible;
                _isMove = false;
                CrosshairDragged?.Invoke(this, e);
            }
        }
    }
}
