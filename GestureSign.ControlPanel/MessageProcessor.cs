using GestureSign.Common.Gestures;
using GestureSign.Common.InterProcessCommunication;
using GestureSign.Common.Log;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Point = System.Drawing.Point;

namespace GestureSign.ControlPanel
{
    class MessageProcessor : IMessageProcessor
    {
        public static event EventHandler<RecordedGestureDefinitionResult> GotNewGestureDefinition;

        public bool ProcessMessages(IpcCommands command, object data)
        {
            try
            {
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    switch (command)
                    {
                        case IpcCommands.Exit:
                            {
                                Application.Current.Shutdown();
                                break;
                            }
                        case IpcCommands.GotGesture:
                            {
                                var newDefinition = data as RecordedGestureDefinitionResult;
                                if (newDefinition != null)
                                {
                                    GotNewGestureDefinition?.Invoke(this, newDefinition);
                                    break;
                                }

                                Logging.LogWarning($"[MessageProcessor] Unexpected gesture data type: {data?.GetType().FullName ?? "null"}");
                                break;
                            }
                    }
                }, DispatcherPriority.Input);

                return true;
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
                return false;
            }

        }
    }
}
