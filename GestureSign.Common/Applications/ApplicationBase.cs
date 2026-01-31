using ManagedWinapi.Windows;
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
        /// 匹配条件列表（多条件 AND 组合）
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public virtual List<MatchCondition> MatchConditions { get; set; } = new List<MatchCondition>();

        [DefaultValue("")]
        public virtual string Group { get; set; }

        /// <summary>
        /// 是否启用优先级窗口检测（触摸板 ActiveWindow 模式下）
        /// </summary>
        [DefaultValue(false)]
        public virtual bool DetectPriorityWindowByMousePosition { get; set; } = false;

        /// <summary>
        /// 优先级窗口列表
        /// 外层 List：多个优先级窗口（OR 关系，按顺序匹配）
        /// 内层 List：每个优先级窗口的匹配条件（AND 关系，全部满足才匹配）
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public virtual List<List<MatchCondition>> PriorityWindows { get; set; } = new List<List<MatchCondition>>();

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

        public bool IsSystemWindowMatch(SystemWindow Window)
        {
            return WindowMatcher.IsMatch(Window, this);
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
