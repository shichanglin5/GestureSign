using System.Windows.Controls;

namespace GestureSign.CorePlugins
{
    public partial class NextApplicationUI : UserControl
    {
        public NextApplicationUI()
        {
            InitializeComponent();
        }

        public bool SkipMinimizedWindows
        {
            get => SkipMinimizedCheckBox.IsChecked ?? true;
            set => SkipMinimizedCheckBox.IsChecked = value;
        }
    }
}
