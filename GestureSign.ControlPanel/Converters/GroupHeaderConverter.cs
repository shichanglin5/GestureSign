using System;
using System.Windows.Data;
using GestureSign.Common.Localization;

namespace GestureSign.ControlPanel.Converters
{
    public class GroupHeaderConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            int fingerCount = (int)values[0];
            int count = (int)values[1];

            string groupName;
            if (fingerCount == 0)
            {
                groupName = "未分类手势";
            }
            else if (fingerCount == 1)
            {
                groupName = "单指手势";
            }
            else
            {
                groupName = $"{fingerCount}指手势";
            }

            return $"{groupName} ({count})";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
        {
            return new[] { Binding.DoNothing, Binding.DoNothing };
        }
    }
}