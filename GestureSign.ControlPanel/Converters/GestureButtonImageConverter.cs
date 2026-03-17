using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using GestureSign.ControlPanel.Common;

namespace GestureSign.ControlPanel.Converters
{
    public class GestureButtonImageConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var gestureMap = values[0] as Dictionary<string, GestureItem>;
            string gestureId = values.Length > 1 ? values[1] as string : null;
            string gestureName = values.Length > 2 ? values[2] as string : null;

            if (gestureMap == null)
                return null;

            GestureItem gi = null;
            if (!string.IsNullOrEmpty(gestureId) && gestureMap.TryGetValue(gestureId, out gi))
                return gi?.GestureImage;

            if (!string.IsNullOrEmpty(gestureName) && gestureMap.TryGetValue(gestureName, out gi))
                return gi?.GestureImage;

            if (!string.IsNullOrEmpty(gestureName))
            {
                gi = gestureMap.Values.FirstOrDefault(item => item?.Gesture?.Name == gestureName);
                if (gi != null)
                    return gi.GestureImage;
            }

            return null;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            return new object[] { Binding.DoNothing, Binding.DoNothing };
        }
    }
}
