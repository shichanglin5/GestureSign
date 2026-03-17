using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using GestureSign.Common.Gestures;
using ManagedWinapi.Windows;

namespace GestureSign.Common.Applications
{
    public interface IApplicationManager
    {
        bool ApplicationExists(string ApplicationName);
        List<IApplication> Applications { get; }
        SystemWindow CaptureWindow { get; }
        void AddApplication(IApplication Application);
        IEnumerable<IAction> GetRecognizedDefinedAction(string gestureId);
        IEnumerable<IApplication> GetApplicationFromPoint(Point testPoint);
        IApplication[] GetApplicationFromWindow(SystemWindow Window, bool userApplicationOnly);
        IApplication[] GetAvailableUserApplications();
        IEnumerable<IAction> GetDefinedAction(string gestureId, IEnumerable<IApplication> Application, bool UseGlobal);
        IEnumerable<ICommand> GetRecognizedTapCommands(int fingerCount, GestureModifiers modifiers);
        IEnumerable<ICommand> GetRecognizedTapCommands(string gestureId, int fingerCount, GestureModifiers modifiers);
        IEnumerable<TapGestureConfig> GetRecognizedTapDefinitions(int fingerCount, GestureModifiers modifiers);
        IEnumerable<TipTapGestureConfig> GetRecognizedTipTapConfigs(int fingerCount, GestureModifiers modifiers);
        IEnumerable<TipTapGestureConfig> GetRecognizedTipTapConfigsByFixCount(int fixFingerCount, GestureModifiers modifiers);
        IEnumerable<TapGestureConfig> GetGlobalTapDefinitions(int fingerCount, GestureModifiers modifiers);
        IEnumerable<TipTapGestureConfig> GetGlobalTipTapDefinitions(int fingerCount);
        IEnumerable<TipTapGestureConfig> GetGlobalTipTapDefinitionsByFixCount(int fixFingerCount);
        IApplication GetExistingUserApplication(string ApplicationName);
        IApplication GetGlobalApplication();
        SystemWindow GetWindowFromPoint(Point Point);
        Task LoadApplications();
        bool SaveApplications();
    }
}
