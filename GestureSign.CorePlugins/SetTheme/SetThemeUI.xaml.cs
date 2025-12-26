using GestureSign.Common.Plugins;
using System.Windows;
using System.Windows.Controls;

namespace GestureSign.CorePlugins.SetTheme
{
    /// <summary>
    /// Interaction logic for SetThemeUI.xaml
    /// </summary>
    public partial class SetThemeUI : UserControl
    {
        #region Private Variables

        private SetThemeSettings _settings;

        #endregion

        #region Public Properties

        public SetThemeSettings Settings
        {
            get
            {
                _settings = _settings ?? new SetThemeSettings();

                // Get checkbox states
                _settings.ToggleAppTheme = ToggleAppThemeCheckBox.IsChecked == true;
                _settings.ToggleSystemTheme = ToggleSystemThemeCheckBox.IsChecked == true;

                return _settings;
            }
            set
            {
                _settings = value ?? new SetThemeSettings();

                // Set checkbox states
                ToggleAppThemeCheckBox.IsChecked = _settings.ToggleAppTheme;
                ToggleSystemThemeCheckBox.IsChecked = _settings.ToggleSystemTheme;
            }
        }

        public IHostControl HostControl { get; set; }

        #endregion

        #region Constructors

        public SetThemeUI()
        {
            InitializeComponent();
        }

        #endregion
    }
}
