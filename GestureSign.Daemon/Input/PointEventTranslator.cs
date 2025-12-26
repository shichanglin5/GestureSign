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

        /// <summary>
        /// Creates virtual contacts for touchpad to maintain finger count
        /// Virtual contacts follow the trajectory of valid contacts
        /// </summary>
        private List<RawData> CreateVirtualContacts(List<RawData> validContacts, int totalCount, Devices sourceDevice)
        {
            if (sourceDevice != Devices.TouchPad || validContacts.Count >= totalCount || validContacts.Count < 2)
                return validContacts;

            int virtualCount = totalCount - validContacts.Count;

            // Find unique IDs that don't exist in valid contacts
            var existingIds = new HashSet<int>(validContacts.Select(c => c.ContactIdentifier));
            int nextVirtualId = 100;
            while (existingIds.Contains(nextVirtualId))
                nextVirtualId++;

            var referenceContact = validContacts[0]; // Use first valid contact as reference

            for (int i = 0; i < virtualCount; i++)
            {
                // Create virtual contact that follows the reference contact with unique ID
                validContacts.Add(new RawData(referenceContact.State, nextVirtualId + i, referenceContact.RawPoints));
            }

            return validContacts;
        }

        private void TranslateTouchEvent(object sender, RawPointsDataMessageEventArgs e)
        {
            GestureSign.Common.Log.Logging.LogWarning($"[PointEventTranslator] RawData.Count={e.RawData.Count}, _lastPointsCount={_lastPointsCount}, SourceDevice={e.SourceDevice}");
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
                        OnPointUp(new InputPointsEventArgs(e.RawData, e.SourceDevice));
                        _lastPointsCount = 0;
                        return;
                    }

                    var contactsToSend = CreateVirtualContacts(validContacts, e.RawData.Count, e.SourceDevice);

                    // For touchscreen: if valid contact count changed significantly, trigger PointUp
                    if (e.SourceDevice != Devices.TouchPad && validContacts.Count < _lastPointsCount - 1)
                    {
                        OnPointUp(new InputPointsEventArgs(validContacts, e.SourceDevice));
                        _lastPointsCount = validContacts.Count;
                        return;
                    }

                    OnPointMove(new InputPointsEventArgs(contactsToSend, e.SourceDevice));
                }
                else if (e.RawData.Count > _lastPointsCount)
                {
                    if (PointCapture.Instance.InputPoints.Any(p => p.Count > 10))
                    {
                        OnPointMove(new InputPointsEventArgs(e.RawData, e.SourceDevice));
                        return;
                    }

                    var validContacts = FilterValidContacts(e.RawData, e.SourceDevice);

                    if (validContacts.Count == 0)
                        return;  // No valid contacts, skip this event

                    var contactsToSend = CreateVirtualContacts(validContacts, e.RawData.Count, e.SourceDevice);

                    // Update last points count based on device type
                    _lastPointsCount = (e.SourceDevice == Devices.TouchPad && contactsToSend.Count > validContacts.Count)
                        ? e.RawData.Count  // Use total count for touchpad with virtual contacts
                        : validContacts.Count;  // Use valid count for other devices

                    OnPointDown(new InputPointsEventArgs(contactsToSend, e.SourceDevice));
                }
                else
                {
                    GestureSign.Common.Log.Logging.LogWarning($"[PointEventTranslator] Triggering PointUp - RawData.Count={e.RawData.Count}, releaseCount={releaseCount}");
                    OnPointUp(new InputPointsEventArgs(e.RawData, e.SourceDevice));
                    _lastPointsCount = _lastPointsCount - e.RawData.Count > releaseCount ? e.RawData.Count : _lastPointsCount - releaseCount;
                }
            }
        }

        #endregion
    }
}
