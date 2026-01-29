using ManagedWinapi.Windows;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace GestureSign.Common.Applications
{
    public interface IApplication : INotifyCollectionChanged
    {
        string Name { get; set; }

        IEnumerable<IAction> Actions { get; set; }

        /// <summary>
        /// 匹配条件列表（多条件 AND 组合）
        /// </summary>
        List<MatchCondition> MatchConditions { get; set; }

        string Group { get; set; }

        void AddAction(IAction Action);
        void Insert(int index, IAction action);
        void RemoveAction(IAction Action);
        void MoveAction(int oldIndex, int newIndex);
        bool IsSystemWindowMatch(SystemWindow Window);
    }
}
