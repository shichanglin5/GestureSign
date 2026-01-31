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

        /// <summary>
        /// 鼠标位置窗口检测模式（触摸板 ActiveWindow 模式下）
        /// </summary>
        MouseWindowDetectionMode MouseWindowDetection { get; set; }

        /// <summary>
        /// 优先级窗口列表
        /// 外层 List：多个优先级窗口（OR 关系，按顺序匹配）
        /// 内层 List：每个优先级窗口的匹配条件（AND 关系，全部满足才匹配）
        /// </summary>
        List<List<MatchCondition>> PriorityWindows { get; set; }

        void AddAction(IAction Action);
        void Insert(int index, IAction action);
        void RemoveAction(IAction Action);
        void MoveAction(int oldIndex, int newIndex);
        bool IsSystemWindowMatch(SystemWindow Window);
    }
}
