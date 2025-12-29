using System;
using System.Collections.Generic;
using System.Linq;
using GestureSign.Common.Input;

namespace GestureSign.Daemon.Input
{
    public class InputPointsEventArgs : EventArgs
    {
        #region Constructors

        public InputPointsEventArgs(List<InputPoint> inputPointList, Devices pointSource, int totalFingerCount = 0)
        {
            InputPointList = inputPointList;
            PointSource = pointSource;
            TotalFingerCount = totalFingerCount > 0 ? totalFingerCount : inputPointList?.Count ?? 0;
        }

        public InputPointsEventArgs(List<RawData> rawDataList, Devices pointSource, int totalFingerCount = 0)
        {
            InputPointList = rawDataList?.Select(rd => new InputPoint(rd.ContactIdentifier, rd.RawPoints)).ToList();
            PointSource = pointSource;
            TotalFingerCount = totalFingerCount > 0 ? totalFingerCount : InputPointList?.Count ?? 0;
        }

        #endregion

        #region Public Properties

        public List<InputPoint> InputPointList { get; set; }

        public bool Handled { get; set; }

        public Devices PointSource { get; set; }

        /// <summary>
        /// Total number of fingers involved in the gesture (may be greater than InputPointList.Count
        /// when some fingers have been lifted or filtered out)
        /// </summary>
        public int TotalFingerCount { get; set; }

        #endregion
    }
}
