using System.Windows.Media.Imaging;

namespace GestureSign.ControlPanel.Common
{
    /// <summary>
    /// 用于显示应用程序列表项的视图模型
    /// </summary>
    public class ApplicationListViewItem
    {
        #region Public Properties

        public BitmapSource ApplicationIcon { get; set; }
        public string WindowTitle { get; set; }
        public string WindowClass { get; set; }
        public string WindowFilename { get; set; }
        public string ApplicationName { get; set; }

        // 新增字段支持高级匹配
        public string AUMID { get; set; }
        public string ProcessPath { get; set; }

        #endregion
    }
}
