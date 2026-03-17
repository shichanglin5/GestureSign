using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using ManagedWinapi.Windows;

namespace GestureSign.Common.Applications
{
    public interface IApplicationManager
    {
        bool ApplicationExists(string ApplicationName);
        List<IApplication> Applications { get; }
        SystemWindow CaptureWindow { get; }
        void AddApplication(IApplication Application);
        IEnumerable<IAction> GetRecognizedDefinedAction(string GestureName);
        IEnumerable<IAction> GetRecognizedDefinedAction(string gestureId, string gestureName);
        IEnumerable<IApplication> GetApplicationFromPoint(Point testPoint);
        IApplication[] GetApplicationFromWindow(SystemWindow Window, bool userApplicationOnly);
        IApplication[] GetAvailableUserApplications();
        IEnumerable<IAction> GetDefinedAction(string GestureName, IEnumerable<IApplication> Application, bool UseGlobal);
        IEnumerable<IAction> GetDefinedAction(string gestureId, string gestureName, IEnumerable<IApplication> Application, bool UseGlobal);
        IEnumerable<ICommand> GetRecognizedTapCommands(int fingerCount);
        IEnumerable<ICommand> GetRecognizedTapCommands(string gestureId, int fingerCount);
        IEnumerable<ICommand> GetRecognizedClickCommands(string gestureId, int fingerCount);
        IEnumerable<TapGestureConfig> GetRecognizedTapDefinitions(int fingerCount);
        IEnumerable<ClickGestureConfig> GetRecognizedClickDefinitions(int fingerCount);
        IEnumerable<TipTapGestureConfig> GetRecognizedTipTapConfigs(int fingerCount);
        IEnumerable<TipTapGestureConfig> GetRecognizedTipTapConfigsByFixCount(int fixFingerCount);
        IEnumerable<TapGestureConfig> GetGlobalTapDefinitions(int fingerCount);
        IEnumerable<ClickGestureConfig> GetGlobalClickDefinitions(int fingerCount);
        IEnumerable<TipTapGestureConfig> GetGlobalTipTapDefinitions(int fingerCount);
        IEnumerable<TipTapGestureConfig> GetGlobalTipTapDefinitionsByFixCount(int fixFingerCount);
        IApplication GetExistingUserApplication(string ApplicationName);
        IApplication GetGlobalApplication();
        SystemWindow GetWindowFromPoint(Point Point);
        Task LoadApplications();
        bool SaveApplications();
    }
}



