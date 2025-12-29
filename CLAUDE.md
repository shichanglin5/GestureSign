# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

GestureSign is a gesture recognition software for Windows tablets that allows users to automate tasks by drawing gestures with fingers, pen, or mouse. The application uses a multi-process architecture with a background service (Daemon) and a configuration UI (ControlPanel).

## Build Commands

### Building the Solution
```powershell
# Build entire solution (default: Debug configuration)
msbuild GestureSign.sln

# Build specific configuration
msbuild GestureSign.sln /p:Configuration=Release
msbuild GestureSign.sln /p:Configuration=Debug
msbuild GestureSign.sln /p:Configuration=Portable
msbuild GestureSign.sln /p:Configuration=Centennial
msbuild GestureSign.sln /p:Configuration=uiAccessRelease

# Clean and rebuild
msbuild GestureSign.sln /t:Clean,Build /p:Configuration=Release
```

### Build Configurations
- **Debug**: Development build with full debugging symbols
- **Release**: Optimized production build
- **Portable**: Standalone portable version
- **Centennial**: Windows Store (AppX) package build
- **uiAccessRelease**: Build with UIAccess permissions for elevated window interaction

### Output Locations
All projects build to shared bin directories:
- `bin/Debug/` - Debug builds
- `bin/Release/` - Release builds
- `bin/Portable/` - Portable builds
- `bin/Centennial/` - Store package builds
- `bin/uiAccessRelease/` - UIAccess builds

### Building Individual Projects
```powershell
# Build only the Daemon
msbuild GestureSign.Daemon\GestureSign.Daemon.csproj /p:Configuration=Release

# Build only the ControlPanel
msbuild GestureSign.ControlPanel\GestureSign.ControlPanel.csproj /p:Configuration=Release

# Build a plugin
msbuild GestureSign.CorePlugins\GestureSign.CorePlugins.csproj /p:Configuration=Release
```

### Running the Application
```powershell
# Run the Daemon (gesture recognition service)
.\bin\Debug\GestureSign.exe

# Run the ControlPanel (configuration UI)
.\bin\Debug\GestureSignControlPanel.exe
```

## Architecture Overview

### Component Structure

**GestureSign.Daemon** - Background gesture recognition service
- Entry point: `Program.cs` with WinForms message loop
- Captures touch/pen/mouse input via Windows Raw Input API
- Performs gesture pattern matching using `PointPatternAnalyzer`
- Executes plugin actions when gestures are recognized
- Runs in system tray, communicates with ControlPanel via named pipes

**GestureSign.ControlPanel** - WPF configuration application
- Entry point: `App.xaml.cs`
- Provides UI for defining gestures and mapping them to applications
- Manages application-specific gesture configurations
- Synchronizes changes with Daemon via IPC

**GestureSign.Common** - Shared library for domain models and infrastructure
- Core managers: `GestureManager`, `ApplicationManager`, `PluginManager`
- IPC infrastructure: `NamedPipe`, `IMessageProcessor`
- Configuration: `AppConfig`, `FileManager`
- Shared interfaces: `IGesture`, `IApplication`, `IPlugin`, `IPointCapture`

**GestureSign.PointPatterns** - Gesture pattern matching algorithm
- `PointPatternAnalyzer`: Compares captured gestures against stored patterns
- Uses angular margin calculations for pattern recognition
- Default match threshold: 80% probability

**GestureSign.CorePlugins** - Built-in action plugins
- Implements window control, keyboard simulation, mouse actions, etc.
- Each plugin implements `IPlugin` interface

**ManagedWinapi** - Windows API P/Invoke wrapper library

**WindowsInput** - Low-level input simulation library

### Event Flow Architecture

```
Hardware Input (Touch/Pen/Mouse)
    ↓
InputProvider (Raw Input API hooks via MessageWindow)
    ↓
PointEventTranslator (translates to PointDown/Move/Up)
    ↓
PointCapture (singleton event hub)
    ├─→ CaptureStarted event
    │   └─→ ApplicationManager determines active window context
    ├─→ PointCaptured events (during gesture)
    ├─→ BeforePointsCaptured event
    │   └─→ GestureManager performs pattern matching
    │       └─→ Fires GestureRecognized event on match
    │           └─→ PluginManager executes action plugins
    └─→ CaptureEnded event
```

### Inter-Process Communication (IPC)

Daemon and ControlPanel communicate via named pipes (`NamedPipe` class):

**IPC Commands** (see `IpcCommands` enum):
- `LoadGestures` - Daemon reloads gesture definitions
- `LoadApplications` - Daemon reloads application mappings
- `LoadConfiguration` - Daemon reloads global config
- `StartTeaching` - Enter gesture teaching mode
- `StopTraining` - Exit gesture teaching mode
- `GotGesture` - Notify ControlPanel of captured gesture
- `ConfigReload` - Configuration changed notification
- `SynDeviceState` - Synchronize device state
- `Exit` - Shutdown Daemon

Each application implements `IMessageProcessor` to handle incoming IPC messages.

### Component Initialization Order

**Daemon startup** (`GestureSign.Daemon/Program.cs`):
1. Mutex check (ensure single instance)
2. Load localization
3. Initialize in order:
   - `PointCapture.Load()` - Input capture system
   - `TriggerManager.Load()` - Gesture triggers (mouse, hotkey, continuous)
   - `GestureManager.Load()` - Load gesture definitions
   - `ApplicationManager.Load()` - Load application mappings
   - `PluginManager.Load()` - Discover and initialize plugins
4. Create `HostControl` and pass to plugins
5. `TrayManager.Load()` - Setup system tray
6. Start `NamedPipe` server for IPC
7. `Application.Run()` - Start message loop

**ControlPanel startup** (`GestureSign.ControlPanel/App.xaml.cs`):
1. Mutex check (single instance, activate existing if running)
2. Load localization
3. Initialize components:
   - `GestureManager.Load()`
   - `PluginManager.Load()`
   - `ApplicationManager.Load()`
4. Start `NamedPipe` server
5. Show `MainWindow` (WPF)
6. Subscribe to save events to notify Daemon via IPC

### Plugin System

**Plugin Discovery**:
- `PluginManager.Load()` scans plugin directories for DLL files
- Uses reflection to find types implementing `IPlugin`
- Creates `IPluginInfo` wrappers with metadata

**Plugin Lifecycle**:
1. Discovery via reflection
2. `HostControl` property set (provides access to managers)
3. `Initialize()` called with settings
4. `Gestured()` called when gesture recognized
5. Settings persisted via `Serialize()`/`Deserialize()`

**Creating New Plugins**:
- Implement `IPlugin` interface from `GestureSign.Common`
- Access core functionality via `IHostControl`:
  - `ApplicationManager` - application context
  - `GestureManager` - gesture definitions
  - `PointCapture` - input events
  - `PluginManager` - other plugins
  - `TrayManager` - system tray
- Place compiled DLL in plugins directory

### Gesture Pattern Matching

**Pattern Recognition** (`GestureSign.PointPatterns/PointPatternAnalyzer.cs`):
- Captured points interpolated to fixed precision (default: 100 points)
- Angular margin calculation between captured and stored patterns
- Probability score based on angular delta differences
- Match threshold: typically 80% (configurable)

**Hierarchical Gestures**:
- Supports multi-level gesture sequences (e.g., draw L then R)
- Gesture stack with 800ms timeout between levels
- Configured via gesture level settings

### Key Design Patterns

**Singleton Pattern**: All managers (`GestureManager`, `ApplicationManager`, `PluginManager`, `PointCapture`, `TrayManager`) use static singleton instances for global state.

**Observer Pattern**: Event-driven communication between components. `PointCapture` publishes events, managers subscribe.

**Manager Pattern**: `*Manager` classes orchestrate related functionality and lifecycle.

**Plugin Architecture**: `IPlugin` interface enables extensibility without modifying core code.

**Strategy Pattern**: Device-specific input strategies (`TouchScreenDevice`, `TouchPadDevice`, `PenDevice`, `HidDevice`) all implement `IDevice`.

**Named Pipe IPC**: Custom binary protocol for process-to-process communication with `BinaryFormatter` serialization.

## Important Code Locations

**Entry Points**:
- `GestureSign.Daemon/Program.cs:712` - Daemon initialization
- `GestureSign.ControlPanel/App.xaml.cs` - ControlPanel startup

**Core Gesture Recognition**:
- `GestureSign.Daemon/Input/PointCapture.cs` - Input event hub
- `GestureSign.Common/Gestures/GestureManager.cs` - Gesture matching logic
- `GestureSign.PointPatterns/PointPatternAnalyzer.cs` - Pattern matching algorithm

**Plugin System**:
- `GestureSign.Common/Plugins/PluginManager.cs` - Plugin lifecycle
- `GestureSign.Common/Plugins/IPlugin.cs` - Plugin interface
- `GestureSign.Common/Plugins/HostControl.cs` - Plugin API surface

**IPC Infrastructure**:
- `GestureSign.Common/InterProcessCommunication/NamedPipe.cs` - IPC implementation
- `GestureSign.Common/InterProcessCommunication/IMessageProcessor.cs` - Message handler interface

**Input Processing**:
- `GestureSign.Daemon/Input/InputProvider.cs` - Raw input message hooks
- `GestureSign.Daemon/Input/PointEventTranslator.cs` - Event translation
- `GestureSign.Daemon/Input/Devices/*` - Device-specific input handlers

**Configuration**:
- `GestureSign.Common/Configuration/AppConfig.cs` - Global settings
- `GestureSign.Common/Configuration/FileManager.cs` - File persistence

## Development Notes

### Target Framework
All projects target .NET Framework 4.6

### Key Dependencies
- **Newtonsoft.Json** - JSON serialization for configuration
- **MahApps.Metro** - WPF UI framework for ControlPanel
- **Windows Raw Input API** - Low-level input capture
- **Windows Pointer Input API** - Touch/pen input

### Thread Safety
- `SynchronizationContext` used to marshal operations to UI thread
- Input events processed on main thread
- Named pipe IPC runs on background threads

### Security Considerations
- **UIAccess configuration**: The `uiAccessRelease` build enables interaction with elevated windows
- Requires code signing for UIAccess to work
- Mutual exclusion via named mutex prevents multiple instances

### Configuration Files
Stored in user AppData directory:
- Gestures: `*.gest` files (JSON)
- Applications: `*.gapp` files (JSON)
- Settings: `AppConfig.json`

### Localization
- Multi-language support via `LocalizationProvider`
- Resource files in `GestureSign.Common/Localization/`

### Touch Input Hardware Limitations

**Critical Understanding: TouchPad Hardware Only Provides 2 Finger Trajectories Maximum**

Real-world testing reveals that most touchpad hardware/drivers have significant limitations in multi-finger gesture reporting:

**Observed Behavior (from production logs):**
- **4-finger gesture**: HID reports `contactCount=4`, but only provides 2 actual touch coordinates
  ```
  Sending 4 touches (totalFingerCount=4): [0:Tip, 1:Tip, 0:None, 0:None]
  ```
  - 4 slots allocated (indicating 4 fingers detected)
  - Only ContactID 0 and 1 have valid coordinates (State=Tip)
  - ContactID 2 and 3 have State=None (no coordinate data)

- **3-finger gesture**: HID reports `contactCount=3`, but only provides 2 actual touch coordinates
  ```
  Sending 3 touches (totalFingerCount=3): [0:Tip, 1:Tip, 0:None]
  Sending 3 touches (totalFingerCount=3): [0:Tip, 2:Tip, 0:None]
  ```
  - 3 slots allocated
  - Only 2 contacts have valid coordinates
  - 1 slot has State=None

**Architectural Implications:**

1. **Finger Count vs Trajectory Count**:
   - `TotalFingerCount` (from `_outputTouchs.Count`): Total fingers detected by hardware (4, 3, 2, 1)
   - Actual trajectory data: Usually limited to 2 fingers maximum
   - After feature finger selection: May be reduced to 1 trajectory for pattern matching

2. **Why We Need TotalFingerCount Parameter**:
   - Cannot rely on InputPointList.Count alone - it only contains valid coordinates
   - Need to preserve the original finger count detected by HID layer
   - Used to distinguish 2-finger vs 3-finger vs 4-finger gestures
   - Critical for gesture recognition even when trajectory data is incomplete

3. **HID Data Completeness**:
   - `_requiringContactCount`: Countdown of expected contacts from HID header
   - Frequently ends >0 on finger lift (incomplete HID packet)
   - Solution: Send all collected data regardless, let PointEventTranslator detect finger changes

4. **Historical Context - Virtual Touch Contacts (Removed)**:
   - Previous implementation used "virtual contacts" to pad missing finger data
   - Virtual contacts copied feature finger trajectory to maintain finger count
   - Refactored to explicit `TotalFingerCount` parameter for clarity
   - Achieves same result with simpler, more maintainable code

**Key Code Locations**:
- `MessageWindow.cs:407`: `totalFingerCount = _outputTouchs.Count` (slot count, not coordinate count)
- `MessageWindow.cs:414`: Sends data even when `_requiringContactCount > 0`
- `PointEventTranslator.cs:88`: Passes `OriginalContactCount` through pipeline
- `PointCapture.cs:156`: Uses `TotalFingerCount` for gesture finger count
- Log analysis: Search for "totalFingerCount=" to see actual hardware behavior

**Important**: When debugging multi-finger gestures, always check logs for the pattern `[ContactID:State, ...]` to understand which fingers have actual trajectory data vs just presence detection.
