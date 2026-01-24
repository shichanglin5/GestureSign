using GestureSign.Common.Configuration;
using GestureSign.Common.Input;
using GestureSign.Common.InterProcessCommunication;
using Microsoft.Win32;
using System;
using System.Threading.Tasks;

namespace GestureSign.Daemon.Input
{
    internal class InputProvider : IDisposable
    {
        private bool disposedValue = false; // To detect redundant calls
        private MessageWindow _messageWindow;
        private CustomNamedPipeServer _deviceStateServer;
        private int _stateUpdating;

        public event RawPointsDataMessageEventHandler PointsIntercepted;

        public InputProvider()
        {
            _messageWindow = new MessageWindow();
            _messageWindow.PointsIntercepted += MessageWindow_PointsIntercepted;

            AppConfig.ConfigChanged += AppConfig_ConfigChanged;

            SystemEvents.SessionSwitch += new SessionSwitchEventHandler(OnSessionSwitch);
            SystemEvents.PowerModeChanged += new PowerModeChangedEventHandler(OnPowerModeChanged);

            _deviceStateServer = new CustomNamedPipeServer(Common.Constants.Daemon + "DeviceState", IpcCommands.SynDeviceState,
                () => HidDevice.EnumerateDevices());
        }

        private void AppConfig_ConfigChanged(object sender, System.EventArgs e)
        {
            UpdateDeviceState();
        }

        private void MessageWindow_PointsIntercepted(object sender, RawPointsDataMessageEventArgs e)
        {
            if (e.RawData.Count == 0)
                return;
            PointsIntercepted?.Invoke(this, e);
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            GestureSign.Common.Log.Logging.LogInfo($"[InputProvider] PowerModeChanged event received: {e.Mode}");

            if (e.Mode == PowerModes.Resume)
            {
                GestureSign.Common.Log.Logging.LogInfo($"[InputProvider] System resumed from sleep/hibernate, triggering UpdateDeviceState");
                UpdateDeviceState();
            }
            else if (e.Mode == PowerModes.Suspend)
            {
                GestureSign.Common.Log.Logging.LogInfo($"[InputProvider] System suspending (sleep/hibernate)");
            }
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            GestureSign.Common.Log.Logging.LogInfo($"[InputProvider] SessionSwitch event received: {e.Reason}");

            // We need to handle sleeping(and other related events)
            // This is so we never lose the lock on the touchpad hardware.
            switch (e.Reason)
            {
                case SessionSwitchReason.SessionLogon:
                    GestureSign.Common.Log.Logging.LogInfo($"[InputProvider] User logged on, triggering UpdateDeviceState");
                    UpdateDeviceState();
                    break;
                case SessionSwitchReason.SessionUnlock:
                    GestureSign.Common.Log.Logging.LogInfo($"[InputProvider] Session unlocked, triggering UpdateDeviceState");
                    UpdateDeviceState();
                    break;
                case SessionSwitchReason.SessionLock:
                    GestureSign.Common.Log.Logging.LogInfo($"[InputProvider] Session locked");
                    break;
                default:
                    GestureSign.Common.Log.Logging.LogDebug($"[InputProvider] SessionSwitch {e.Reason} - no action taken");
                    break;
            }
        }

        private void UpdateDeviceState()
        {
            if (0 == System.Threading.Interlocked.Exchange(ref _stateUpdating, 1))
            {
                GestureSign.Common.Log.Logging.LogInfo($"[InputProvider] UpdateDeviceState initiated, waiting 600ms for hardware stabilization");
                Task.Delay(600).ContinueWith((t) =>
                {
                    try
                    {
                        System.Threading.Interlocked.Exchange(ref _stateUpdating, 0);
                        GestureSign.Common.Log.Logging.LogInfo($"[InputProvider] 600ms delay complete, calling MessageWindow.UpdateRegistration()");
                        _messageWindow.UpdateRegistration();
                        GestureSign.Common.Log.Logging.LogInfo($"[InputProvider] UpdateDeviceState completed successfully");
                    }
                    catch (Exception ex)
                    {
                        GestureSign.Common.Log.Logging.LogError($"[InputProvider] UpdateDeviceState failed: {ex.Message} - StackTrace: {ex.StackTrace}");
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }
            else
            {
                GestureSign.Common.Log.Logging.LogDebug($"[InputProvider] UpdateDeviceState already in progress, skipping duplicate call");
            }
        }

        #region IDisposable Support

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    AppConfig.ConfigChanged -= AppConfig_ConfigChanged;
                }

                SystemEvents.SessionSwitch -= OnSessionSwitch;
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                _deviceStateServer.Dispose();
                disposedValue = true;
            }
        }

        ~InputProvider()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
