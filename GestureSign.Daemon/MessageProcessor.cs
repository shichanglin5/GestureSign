using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using GestureSign.Common.Gestures;
using GestureSign.Common.Input;
using GestureSign.Common.InterProcessCommunication;
using GestureSign.Daemon.Input;
using System.Linq;
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
            GestureSign.Common.Log.Logging.LogDebug($"[MessageProcessor] Received IPC command: {command}");
            _synchronizationContext.Post(state =>
            {
                switch (command)
                {
                    case IpcCommands.StartTeaching:
                        GestureSign.Common.Log.Logging.LogDebug($"[MessageProcessor] Setting Mode to Training");
                        PointCapture.Instance.Mode = CaptureMode.Training;
                        GestureSign.Common.Log.Logging.LogDebug($"[MessageProcessor] Mode set to: {PointCapture.Instance.Mode}");
                        break;
                    case IpcCommands.StopTraining:
                        GestureSign.Common.Log.Logging.LogDebug($"[MessageProcessor] Setting Mode to Normal");
                        if (PointCapture.Instance.Mode != CaptureMode.UserDisabled)
                            PointCapture.Instance.Mode = CaptureMode.Normal;
                        break;
                    case IpcCommands.LoadApplications:
                        GestureSign.Common.Log.Logging.LogDebug($"[MessageProcessor] Loading applications...");
                        ApplicationManager.Instance.LoadApplications().Wait();
                        GestureSign.Common.Log.Logging.LogDebug($"[MessageProcessor] Applications loaded, count: {ApplicationManager.Instance.Applications.Count()}");
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
