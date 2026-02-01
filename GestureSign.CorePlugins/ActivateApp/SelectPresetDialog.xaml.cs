using System.Windows;
using System.Windows.Input;
using GestureSign.Common.Applications;

namespace GestureSign.CorePlugins.ActivateApp
{
    /// <summary>
    /// SelectPresetDialog for ActivateApp plugin
    /// </summary>
    public partial class SelectPresetDialog : Window
    {
        /// <summary>
        /// 选中的预置规则 ID
        /// </summary>
        public string? SelectedPresetId { get; private set; }

        public SelectPresetDialog()
        {
            InitializeComponent();
            Loaded += SelectPresetDialog_Loaded;
        }

        private void SelectPresetDialog_Loaded(object sender, RoutedEventArgs e)
        {
            PresetsListBox.ItemsSource = WindowPresetManager.Instance.Presets;
            if (PresetsListBox.Items.Count > 0)
            {
                PresetsListBox.SelectedIndex = 0;
            }
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            if (PresetsListBox.SelectedItem is WindowRule preset)
            {
                SelectedPresetId = preset.Id;
                DialogResult = true;
            }
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void PresetsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (PresetsListBox.SelectedItem != null)
            {
                OKButton_Click(sender, e);
            }
        }
    }
}
