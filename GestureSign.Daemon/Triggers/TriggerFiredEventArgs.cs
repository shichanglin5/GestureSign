using GestureSign.Common.Applications;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace GestureSign.Daemon.Triggers
{
    public class TriggerFiredEventArgs : EventArgs
    {
        public TriggerFiredEventArgs(List<IAction> firedActions, Point firedPoint, VelocityVector? velocity = null)
        {
            FiredActions = firedActions;
            FiredPoint = firedPoint;
            Velocity = velocity;
        }

        public List<IAction> FiredActions { get; }
        public Point FiredPoint { get; }

        /// <summary>
        /// 手势滑动速度向量 (可选,仅连续手势触发时提供)
        /// </summary>
        public VelocityVector? Velocity { get; }
    }
}
