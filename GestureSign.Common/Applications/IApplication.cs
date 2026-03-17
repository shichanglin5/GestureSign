using System.Collections.Generic;
using System.Collections.Specialized;

namespace GestureSign.Common.Applications
{
    public interface IApplication : INotifyCollectionChanged
    {
        string Name { get; set; }

        IEnumerable<IAction> Actions { get; set; }

        /// <summary>
        /// 应用匹配规则列表（有序，OR 关系，支持拖动排序）
        /// 任意一个规则匹配即视为此应用匹配
        /// </summary>
        List<IWindowRule> MatchRules { get; set; }

        string Group { get; set; }

        /// <summary>
        /// 鼠标位置窗口检测模式（触摸板 ActiveWindow 模式下）
        /// </summary>
        MouseWindowDetectionMode MouseWindowDetection { get; set; }

        /// <summary>
        /// 优先级窗口列表（有序，OR 关系，支持拖动排序）
        /// </summary>
        List<IWindowRule> PriorityWindows { get; set; }

        ContinuousGestureMode ContinuousGestureMode { get; set; }
        double ZoomSpeed { get; set; }

        ContinuousGestureSettings ContinuousGestures { get; set; }
        TwoFingerGestureSettings TwoFingerGestures { get; set; }
        ContactGestureSettings ContactGestures { get; set; }

        void AddAction(IAction Action);
        void Insert(int index, IAction action);
        void RemoveAction(IAction Action);
        void MoveAction(int oldIndex, int newIndex);
        bool IsMatch(WindowInfoCache windowInfo);
    }
}



