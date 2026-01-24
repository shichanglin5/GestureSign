using System;
using System.Collections.Generic;
using System.Linq;
using GestureSign.Common.Input;

namespace GestureSign.Daemon.Input
{
    public class PointEventTranslator
    {
        private int _lastPointsCount;

        internal Devices SourceDevice { get; private set; }

        internal PointEventTranslator(InputProvider inputProvider)
        {
            inputProvider.PointsIntercepted += TranslateTouchEvent;
        }

        #region Custom Events

        public event EventHandler<InputPointsEventArgs> PointDown;

        protected virtual void OnPointDown(InputPointsEventArgs args)
        {
            if (SourceDevice != Devices.None && SourceDevice != args.PointSource) return;
            SourceDevice = args.PointSource;
            PointDown?.Invoke(this, args);
        }

        public event EventHandler<InputPointsEventArgs> PointUp;

        protected virtual void OnPointUp(InputPointsEventArgs args)
        {
            if (SourceDevice != Devices.None && SourceDevice != args.PointSource) return;

            PointUp?.Invoke(this, args);

            SourceDevice = Devices.None;
        }

        public event EventHandler<InputPointsEventArgs> PointMove;

        protected virtual void OnPointMove(InputPointsEventArgs args)
        {
            if (SourceDevice != args.PointSource) return;
            PointMove?.Invoke(this, args);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Filters out invalid contacts based on device type
        /// </summary>
        private List<RawData> FilterValidContacts(List<RawData> rawData, Devices sourceDevice)
        {
            // Filter out State=None (release state) contacts for all devices
            // State=None (0) indicates the finger has been lifted
            // For touchscreen, also filter out (0,0) coordinates as invalid
            if (sourceDevice == Devices.TouchPad)
            {
                return rawData.Where(rd => rd.State != 0).ToList();
            }
            else
            {
                // For TouchScreen: filter out State=None AND (0,0) coordinates
                return rawData.Where(rd => rd.State != 0 && !(rd.RawPoints.X == 0 && rd.RawPoints.Y == 0)).ToList();
            }
        }


        private void TranslateTouchEvent(object sender, RawPointsDataMessageEventArgs e)
        {
            if ((e.SourceDevice & Devices.TouchDevice) != 0)
            {
                int releaseCount = e.RawData.Count(rtd => rtd.State == 0);
                int activeCount = e.RawData.Count - releaseCount;

                if (e.RawData.Count == _lastPointsCount)
                {
                    var validContacts = FilterValidContacts(e.RawData, e.SourceDevice);

                    // If no valid contacts, all fingers lifted - trigger PointUp
                    if (validContacts.Count == 0)
                    {
                        OnPointUp(new InputPointsEventArgs(e.RawData, e.SourceDevice, e.OriginalContactCount));
                        _lastPointsCount = 0;
                        return;
                    }

                    // For touchscreen: if valid contact count changed significantly, trigger PointUp
                    if (e.SourceDevice != Devices.TouchPad && validContacts.Count < _lastPointsCount - 1)
                    {
                        OnPointUp(new InputPointsEventArgs(validContacts, e.SourceDevice, e.OriginalContactCount));
                        _lastPointsCount = validContacts.Count;
                        return;
                    }

                    // Send only valid contacts, but pass OriginalContactCount for total finger count
                    OnPointMove(new InputPointsEventArgs(validContacts, e.SourceDevice, e.OriginalContactCount));
                }
                else if (e.RawData.Count > _lastPointsCount)
                {
                    if (PointCapture.Instance.InputPoints.Any(p => p.Count > 10))
                    {
                        OnPointMove(new InputPointsEventArgs(e.RawData, e.SourceDevice, e.OriginalContactCount));
                        return;
                    }

                    var validContacts = FilterValidContacts(e.RawData, e.SourceDevice);

                    if (validContacts.Count == 0)
                        return;  // No valid contacts, skip this event

                    // Update last points count to match the data count (not original contact count)
                    // This is for PointUp/Down/Move detection based on data count changes
                    _lastPointsCount = e.RawData.Count;

                    OnPointDown(new InputPointsEventArgs(validContacts, e.SourceDevice, e.OriginalContactCount));
                }
                else
                {
                    OnPointUp(new InputPointsEventArgs(e.RawData, e.SourceDevice, e.OriginalContactCount));
                    _lastPointsCount = _lastPointsCount - e.RawData.Count > releaseCount ? e.RawData.Count : _lastPointsCount - releaseCount;
                }
            }
        }

        #endregion
    }
}
