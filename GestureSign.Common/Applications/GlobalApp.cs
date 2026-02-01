using GestureSign.Common.Localization;

namespace GestureSign.Common.Applications
{
    public class GlobalApp : ApplicationBase
    {
        private int _limitNumberOfFingers;

        public int LimitNumberOfFingers
        {
            get { return _limitNumberOfFingers < 2 ? _limitNumberOfFingers = 2 : _limitNumberOfFingers; }
            set { _limitNumberOfFingers = value; }
        }

        #region IApplication Properties

        public override string Name
        {
            get { return LocalizationProvider.Instance.GetTextValue("Common.GlobalActions"); ; }
            //set { /* Set only exists for deserialization purposes */ }
        }

        /// <summary>
        /// GlobalApp 匹配所有窗口
        /// </summary>
        public override bool IsMatch(WindowInfoCache windowInfo)
        {
            return windowInfo != null;
        }

        #endregion
    }
}
