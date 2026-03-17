using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;

namespace GestureSign.Common.Input
{
    public class PointsCapturedEventArgs : EventArgs
    {
        #region Constructors

        public PointsCapturedEventArgs(List<Point> capturePoint)
        {
            this.FirstCapturedPoints = capturePoint;
            this.Points = new List<List<Point>>(capturePoint.Count);
            for (int i = 0; i < capturePoint.Count; i++)
            {
                this.Points.Add(new List<Point>(1));
                this.Points[i].Add(capturePoint[i]);
            }
            this.AllPoints = this.Points;
        }

        public PointsCapturedEventArgs(List<List<Point>> points, List<Point> capturePoint)
        {
            this.Points = points;
            this.FirstCapturedPoints = capturePoint;
            this.AllPoints = points;
        }

        #endregion

        #region Public Properties

        public List<List<Point>> Points { get; set; }
        public List<List<Point>> AllPoints { get; set; }
        public GestureAnalysis Analysis { get; set; }
        public GestureSessionSnapshot Session { get; set; }
        public List<Point> FirstCapturedPoints { get; set; }
        public bool Cancel { get; set; }
        public int BlockTouchInputThreshold { get; set; }
        public int FingerCount { get; set; }
        /// <summary>
        /// 当前实际活跃的手指数（手指抬起后会减少），用于连续手势判断
        /// </summary>
        public int ActiveFingerCount { get; set; }

        #endregion
    }
}
