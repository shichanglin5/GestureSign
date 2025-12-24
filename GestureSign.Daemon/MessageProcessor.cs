using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using GestureSign.Common.Gestures;
using GestureSign.Common.Input;
using GestureSign.Common.InterProcessCommunication;
using GestureSign.Daemon.Input;
using System.Threading;

namespace GestureSign.Daemon
{
    class MessageProcessor : IMessageProcessor
    {
        private SynchronizationContext _synchronizationContext;

        public MessageProcessor(SynchronizationContext synchronizationContext)
        {
            _synchronizationContext = synchronizationContext;
        }

        public bool ProcessMessages(IpcCommands command, object data)
        {
            GestureSign.Common.Log.Logging.LogMessage($"[MessageProcessor] Received IPC command: {command}");
            _synchronizationContext.Post(state =>
            {
                switch (command)
                {
                    case IpcCommands.StartTeaching:
                        GestureSign.Common.Log.Logging.LogMessage($"[MessageProcessor] Setting Mode to Training");
                        PointCapture.Instance.Mode = CaptureMode.Training;
                        GestureSign.Common.Log.Logging.LogMessage($"[MessageProcessor] Mode set to: {PointCapture.Instance.Mode}");
                        break;
                    case IpcCommands.StopTraining:
                        GestureSign.Common.Log.Logging.LogMessage($"[MessageProcessor] Setting Mode to Normal");
                        if (PointCapture.Instance.Mode != CaptureMode.UserDisabled)
                            PointCapture.Instance.Mode = CaptureMode.Normal;
                        break;
                    case IpcCommands.LoadApplications:
                        ApplicationManager.Instance.LoadApplications().Wait();
                        break;
                    case IpcCommands.LoadGestures:
                        GestureManager.Instance.LoadGestures().Wait();
                        break;
                    case IpcCommands.LoadConfiguration:
                        AppConfig.Reload();
                        break;
                    case IpcCommands.StartControlPanel:
                        TrayManager.StartControlPanel();
                        break;
                }
            }, null);

            return true;
        }

    }
}
