using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace GestureSign.Common.Applications
{
    public abstract class ApplicationBase : IApplication, INotifyCollectionChanged, IComparable, IComparable<ApplicationBase>
    {
        #region Private Instance Fields

        List<IAction> _Actions = new List<IAction>();

        #endregion

        #region IApplication Instance Properties

        public virtual string Name { get; set; }

        /// <summary>
        /// 应用匹配规则列表（有序，OR 关系）
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, ItemTypeNameHandling = TypeNameHandling.Auto)]
        public virtual List<IWindowRule> MatchRules { get; set; } = new List<IWindowRule>();

        [DefaultValue("")]
        public virtual string Group { get; set; }

        /// <summary>
        /// 鼠标位置窗口检测模式（触摸板 ActiveWindow 模式下）
        /// </summary>
        [DefaultValue(MouseWindowDetectionMode.Disabled)]
        public virtual MouseWindowDetectionMode MouseWindowDetection { get; set; } = MouseWindowDetectionMode.Disabled;

        /// <summary>
        /// 优先级窗口列表（有序，OR 关系）
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, ItemTypeNameHandling = TypeNameHandling.Auto)]
        public virtual List<IWindowRule> PriorityWindows { get; set; } = new List<IWindowRule>();

        [JsonProperty(ItemTypeNameHandling = TypeNameHandling.None)]
        public virtual IEnumerable<IAction> Actions
        {
            get { return _Actions.AsEnumerable(); }
            set { _Actions = value.ToList(); }
        }

        public virtual event NotifyCollectionChangedEventHandler CollectionChanged;

        #endregion

        #region Private Methods

        protected virtual void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            CollectionChanged?.Invoke(this, e);
        }

        private void OnCollectionChanged(NotifyCollectionChangedAction action, object changedItem)
        {
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(action, changedItem));
        }

        #endregion

        #region IApplication Instance Methods

        public virtual void AddAction(IAction Action)
        {
            _Actions.Add(Action);
            OnCollectionChanged(NotifyCollectionChangedAction.Add, Action);
        }

        public virtual void Insert(int index, IAction action)
        {
            _Actions.Insert(index, action);
            OnCollectionChanged(NotifyCollectionChangedAction.Add, action);
        }

        public virtual void RemoveAction(IAction Action)
        {
            _Actions.Remove(Action);
            OnCollectionChanged(NotifyCollectionChangedAction.Remove, Action);
        }

        public virtual void MoveAction(int oldIndex, int newIndex)
        {
            if (oldIndex < 0 || oldIndex >= _Actions.Count)
                throw new ArgumentOutOfRangeException(nameof(oldIndex));
            if (newIndex < 0 || newIndex >= _Actions.Count)
                throw new ArgumentOutOfRangeException(nameof(newIndex));

            if (oldIndex == newIndex)
                return;

            var action = _Actions[oldIndex];
            _Actions.RemoveAt(oldIndex);
            _Actions.Insert(newIndex, action);

            OnCollectionChanged(new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Move,
                action,
                newIndex,
                oldIndex));
        }

        public virtual bool IsMatch(WindowInfoCache windowInfo)
        {
            if (windowInfo == null)
                return false;

            var matchRules = MatchRules;

            if (matchRules == null || matchRules.Count == 0)
            {
                return false;
            }

            // OR 关系：任一规则匹配即可
            foreach (var rule in matchRules)
            {
                if (rule != null && rule.IsMatch(windowInfo))
                    return true;
            }

            return false;
        }

        public int CompareTo(object obj)
        {
            var item = obj as ApplicationBase;
            return CompareTo(item);
        }

        public int CompareTo(ApplicationBase item)
        {
            if (this is GlobalApp)
            {
                return -1;
            }
            else
            {
                if (item is GlobalApp)
                    return 1;
                return string.Compare(Name, item.Name, false, System.Globalization.CultureInfo.CurrentCulture);
            }
        }

        #endregion
    }
}
