using System;
using System.Drawing;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using WindowsInput;
using GestureSign.Common.Input;
using GestureSign.Common.Localization;
using GestureSign.Common.Log;
using GestureSign.Common.Plugins;

namespace GestureSign.CorePlugins.ActivateFocusWindow
{
    public class ActivateFocusWindowPlugin : IPlugin
    {
        #region Private Variables

        private TextBlock _gui;

        #endregion

        #region IPlugin Properties

        public string Name =>
            LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateFocusWindow.Name");

        public string Category => "Windows";

        public string Description
        {
            get
            {
                string sourceText = GetSourceDeviceText();
                if (sourceText == null)
                    return LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateFocusWindow.Description");

                return $"{Name}（{sourceText}）";
            }
        }

        public bool IsAction => true;

        public object GUI => _gui ?? (_gui = CreateGUI());

        public bool ActivateWindowDefault => false;

        public object Icon => IconSource.Window;

        public IHostControl HostControl { get; set; }

        #endregion

        #region IPlugin Methods

        public void Initialize() { }

        public bool Gestured(PointInfo actionPoint)
        {
            try
            {
                System.Drawing.Point targetPoint = actionPoint.PointLocation[0];

                Cursor.Position = targetPoint;

                InputSimulator simulator = new InputSimulator();
                simulator.Mouse.LeftButtonClick();
                Thread.Sleep(30);

                return true;
            }
            catch (Exception ex)
            {
                Logging.LogError($"[ActivateFocusWindow] Error: {ex.Message}");
                return false;
            }
        }

        public bool Deserialize(string serializedData)
        {
            return true;
        }

        public string Serialize()
        {
            return "";
        }

        #endregion

        #region Private Methods

        private string GetSourceDeviceText()
        {
            var pointCapture = HostControl?.PointCapture;
            if (pointCapture == null)
                return null;

            return pointCapture.SourceDevice.HasFlag(Devices.TouchScreen)
                ? LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateFocusWindow.TouchPosition")
                : LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateFocusWindow.MousePosition");
        }

        private TextBlock CreateGUI()
        {
            return new TextBlock
            {
                Text = LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateFocusWindow.Tip"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(10),
                FontSize = 13,
            };
        }

        #endregion
    }
}
