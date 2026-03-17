using GestureSign.Common.Applications;
using System.Linq;
using GestureSign.Common.Configuration;
using GestureSign.Common.Localization;
using MahApps.Metro.Controls;
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Input;

namespace GestureSign.ControlPanel.Converters
{
    [ValueConversion(typeof(IAction), typeof(string))]
    public class ActionTitleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Handle finger count grouping
            if (value is int fingerCount)
            {
                string groupName;
                if (fingerCount == 0)
                    groupName = "未分类手势";
                else if (fingerCount == 1)
                    groupName = "单指手势";
                else
                    groupName = $"{fingerCount}指手势";
                return groupName;
            }

            var action = value as IAction;
            if (action == null) return null;

            var actionName = string.IsNullOrWhiteSpace(action.Name) ? LocalizationProvider.Instance.GetTextValue("Action.NewAction") : action.Name;

            var globalContact = ApplicationManager.Instance.GetGlobalApplication()?.ContactGestures;
            var tap = globalContact?.Taps?.FirstOrDefault(t => t.Id == action.GestureId);
            var tipTap = globalContact?.TipTaps?.FirstOrDefault(t => t.Id == action.GestureId);
            if (tap != null)
            {
                actionName += "\n[Tap] " + tap.Name;
            }
            else if (tipTap != null)
            {
                actionName += "\n[TipTap] " + tipTap.Name;
            }

            if (action.ContinuousGesture != null)
            {
                actionName += "\n" + LocalizationProvider.Instance.GetTextValue("ActionDialog.Continuous") + ": " +
                    string.Format(LocalizationProvider.Instance.GetTextValue("Action.Fingers"), action.ContinuousGesture.ContactCount) + " " + LocalizationProvider.Instance.GetTextValue("Action." + action.ContinuousGesture.Gesture);
            }
            if (action.Hotkey != null)
            {
                actionName += "\n" + LocalizationProvider.Instance.GetTextValue("ActionDialog.KeyboardHotKey") + ": " +
                    new HotKey(KeyInterop.KeyFromVirtualKey(action.Hotkey.KeyCode), (ModifierKeys)action.Hotkey.ModifierKeys).ToString() + "  ";
            }

            if (!string.IsNullOrWhiteSpace(action.Condition))
                actionName += " [Cond]";

            return actionName;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
