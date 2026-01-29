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

        // GlobalApp 的 MatchConditions 为空列表，表示匹配所有窗口

        #endregion
    }
}
