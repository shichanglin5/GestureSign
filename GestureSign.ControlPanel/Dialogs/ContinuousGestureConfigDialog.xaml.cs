using GestureSign.Common.Applications;
using MahApps.Metro.Controls;

namespace GestureSign.ControlPanel.Dialogs
{
    public partial class ContinuousGestureConfigDialog : MetroWindow
    {
        private readonly IApplication _application;

        public ContinuousGestureConfigDialog(IApplication application)
        {
            InitializeComponent();
            _application = application;

            double speed = application.ZoomSpeed;
            ZoomSpeedSlider.Value = speed > 0 ? speed : 1.0;
        }

        private void OkButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            _application.ZoomSpeed = ZoomSpeedSlider.Value;
            DialogResult = true;
            Close();
        }
    }
}
