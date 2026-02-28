using System.Runtime.InteropServices;
using GestureSign.Common.Localization;
using GestureSign.Common.Plugins;

namespace GestureSign.CorePlugins
{
    public class MaximizeRestore : IPlugin
    {
        #region Private Variables

        private IHostControl _hostControl = null;
        private const int SW_MAXIMIZE = 3;
        private const int SW_RESTORE = 9;

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(System.IntPtr hWnd, int nCmdShow);

        #endregion

        #region Public Properties

        public string Name
        {
            get { return LocalizationProvider.Instance.GetTextValue("CorePlugins.MaximizeRestore.Name"); }
        }

        public string Description
        {
            get { return LocalizationProvider.Instance.GetTextValue("CorePlugins.MaximizeRestore.Description"); }
        }

        public object GUI
        {
            get { return null; }
        }

        public bool ActivateWindowDefault
        {
            get { return true; }
        }

        public string Category
        {
            get { return "Windows"; }
        }

        public bool IsAction
        {
            get { return true; }
        }

        public object Icon => IconSource.MaximizeRestore;

        #endregion

        #region Public Methods

        public void Initialize()
        {
        }

        public void ShowGUI(bool IsNew)
        {
            // Nothing to do here
        }

        public bool Gestured(PointInfo ActionPoint)
        {
            if (ActionPoint?.Window == null)
                return false;

            // Use async ShowWindow to avoid synchronous cross-process window proc blocking.
            bool isMaximized = ActionPoint.Window.WindowState == System.Windows.Forms.FormWindowState.Maximized;
            int showCommand = isMaximized ? SW_RESTORE : SW_MAXIMIZE;
            return ShowWindowAsync(ActionPoint.Window.HWnd, showCommand);
        }

        public bool Deserialize(string SerializedData)
        {
            return true;
            // Nothing to do here
        }

        public string Serialize()
        {
            // Nothing to serialize
            return "";
        }

        #endregion

        #region Host Control

        public IHostControl HostControl
        {
            get { return _hostControl; }
            set { _hostControl = value; }
        }

        #endregion
    }
}
