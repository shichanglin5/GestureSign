using System;
using System.Collections.Generic;

namespace GestureSign.Common.Input
{
    public class RawPointsDataMessageEventArgs : EventArgs
    {
        #region Constructors

        public RawPointsDataMessageEventArgs(List<RawData> rawData, Devices device, int originalContactCount)
        {
            this.RawData = rawData;
            SourceDevice = device;
            OriginalContactCount = originalContactCount;
        }


        #endregion

        #region Public Properties

        public List<RawData> RawData { get; set; }
        public Devices SourceDevice { get; set; }

        /// <summary>
        /// Original contact count reported by HID driver (before filtering)
        /// This represents the total number of fingers, including those with State=None
        /// </summary>
        public int OriginalContactCount { get; set; }

        #endregion
    }
}
