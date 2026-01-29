using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using GestureSign.Common.Applications;

namespace GestureSign.ControlPanel.UserControls
{
    /// <summary>
    /// MatchUsingRadio.xaml 的交互逻辑
    /// </summary>
    public partial class MatchUsingRadio : UserControl
    {
        public MatchUsingRadio()
        {
            InitializeComponent();

            MultiBinding multiBinding = new MultiBinding
            {
                Converter = new MatchUsingConverter(),
                Mode = BindingMode.TwoWay
            };
            multiBinding.Bindings.Add(new Binding("IsChecked") { ElementName = "FileNameRadio" });
            multiBinding.Bindings.Add(new Binding("IsChecked") { ElementName = "TitleRadio" });
            multiBinding.Bindings.Add(new Binding("IsChecked") { ElementName = "ClassRadio" });
            multiBinding.Bindings.Add(new Binding("IsChecked") { ElementName = "AUMIDRadio" });
            multiBinding.Bindings.Add(new Binding("IsChecked") { ElementName = "ClassAndPathRadio" });

            SetBinding(MatchUsingProperty, multiBinding);
        }
        public MatchUsing MatchUsing
        {
            get { return (MatchUsing)GetValue(MatchUsingProperty); }
            set
            {
                SetValue(MatchUsingProperty, value);
                switch (value)
                {
                    case MatchUsing.ExecutableFilename:
                        FileNameRadio.IsChecked = true;
                        break;
                    case MatchUsing.WindowTitle:
                        TitleRadio.IsChecked = true;
                        break;
                    case MatchUsing.WindowClass:
                        ClassRadio.IsChecked = true;
                        break;
                    case MatchUsing.AUMID:
                        AUMIDRadio.IsChecked = true;
                        break;
                    case MatchUsing.ClassNameAndPath:
                        ClassAndPathRadio.IsChecked = true;
                        break;
                    default:
                        FileNameRadio.IsChecked = true;
                        break;
                }
            }
        }
        public static readonly DependencyProperty MatchUsingProperty =
            DependencyProperty.Register("MatchUsing", typeof(MatchUsing), typeof(MatchUsingRadio), new FrameworkPropertyMetadata(MatchUsing.ExecutableFilename));

        public class MatchUsingConverter : IMultiValueConverter
        {
            public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            {
                if (values.Length >= 5)
                {
                    if ((bool)values[0]) return MatchUsing.ExecutableFilename;
                    if ((bool)values[1]) return MatchUsing.WindowTitle;
                    if ((bool)values[2]) return MatchUsing.WindowClass;
                    if ((bool)values[3]) return MatchUsing.AUMID;
                    if ((bool)values[4]) return MatchUsing.ClassNameAndPath;
                }
                return MatchUsing.ExecutableFilename;
            }

            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            {
                return new object[5] { Binding.DoNothing, Binding.DoNothing, Binding.DoNothing, Binding.DoNothing, Binding.DoNothing };
            }
        }
    }
}
