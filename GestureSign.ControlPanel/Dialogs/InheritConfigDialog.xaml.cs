using GestureSign.Common.Applications;
using MahApps.Metro.Controls;
using System.Windows;

namespace GestureSign.ControlPanel.Dialogs
{
    public partial class InheritConfigDialog : MetroWindow
    {
        private readonly ContinuousGestureSettings _settings;

        public InheritConfigDialog(ContinuousGestureSettings settings)
        {
            _settings = settings;
            InitializeComponent();

            Inherit2Finger.IsChecked = settings.IsInherited(2);
            Inherit3Finger.IsChecked = settings.IsInherited(3);
            Inherit4Finger.IsChecked = settings.IsInherited(4);
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            _settings.SetInherited(2, Inherit2Finger.IsChecked == true);
            _settings.SetInherited(3, Inherit3Finger.IsChecked == true);
            _settings.SetInherited(4, Inherit4Finger.IsChecked == true);
            DialogResult = true;
        }
    }
}
