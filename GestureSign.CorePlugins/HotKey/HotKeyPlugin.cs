using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using WindowsInput;
using WindowsInput.Native;
using GestureSign.Common.Localization;
using GestureSign.Common.Log;
using GestureSign.Common.Plugins;
using ManagedWinapi;

#pragma warning disable CA1416 // Platform-specific API

namespace GestureSign.CorePlugins.HotKey
{
    public class HotKeyPlugin : IPlugin
    {
        #region Private Variables

        private HotKey _GUI;
        private HotKeySettings _Settings;
        private const string User32 = "user32.dll";

        #endregion

        #region PInvoke Declarations

        [DllImport(User32)]
        private static extern bool LockWorkStation();

        [DllImport(User32)]
        private static extern int GetKeyNameText(int lParam, [Out] StringBuilder lpString, int nSize);

        [DllImport(User32)]
        private static extern int MapVirtualKey(int uCode, int uMapType);

        #endregion


        #region Public Properties

        public string Name
        {
            get { return LocalizationProvider.Instance.GetTextValue("CorePlugins.HotKey.Name"); }
        }

        public string Description
        {
            get { return GetDescription(_Settings); }
        }

        public object GUI
        {
            get { return _GUI ?? (_GUI = CreateGUI()); }
        }

        public bool ActivateWindowDefault
        {
            get { return true; }
        }

        public HotKey TypedGUI
        {
            get { return (HotKey)GUI; }
        }

        public string Category
        {
            get { return LocalizationProvider.Instance.GetTextValue("CorePlugins.HotKey.Category"); }
        }

        public bool IsAction
        {
            get { return true; }
        }

        public object Icon => IconSource.Keyboard;

        #endregion

        #region Public Methods

        public static string GetKeyName(Keys key)
        {
            bool extended;
            switch (key)
            {
                case Keys.VolumeDown:
                case Keys.VolumeMute:
                case Keys.VolumeUp:
                case Keys.MediaNextTrack:
                case Keys.MediaPlayPause:
                case Keys.MediaPreviousTrack:
                case Keys.MediaStop:
                case Keys.BrowserBack:
                case Keys.BrowserForward:
                case Keys.BrowserHome:
                case Keys.BrowserRefresh:
                case Keys.BrowserSearch:
                case Keys.BrowserStop:
                    return key.ToString();
                case Keys.Insert:
                case Keys.Delete:
                case Keys.PageUp:
                case Keys.PageDown:
                case Keys.Home:
                case Keys.End:
                case Keys.Up:
                case Keys.Down:
                case Keys.Left:
                case Keys.Right:
                    extended = true;
                    break;
                default:
                    extended = false;
                    break;
            }
            StringBuilder sb = new StringBuilder(64);
            int scancode = MapVirtualKey((int)key, 0);
            if (extended)
                scancode += 0x100;
            GetKeyNameText(scancode << 16, sb, sb.Capacity);
            if (sb.Length == 0)
            {
                switch (key)
                {
                    case Keys.BrowserBack:
                        sb.Append("Back");
                        break;
                    case Keys.BrowserForward:
                        sb.Append("Forward");
                        break;
                    case (Keys)19:
                        sb.Append("Break");
                        break;
                    case Keys.Apps:
                        sb.Append("ContextMenu");
                        break;
                    case Keys.LWin:
                    case Keys.RWin:
                        sb.Append("Windows");
                        break;
                    case Keys.PrintScreen:
                        sb.Append("PrintScreen");
                        break;
                }
            }
            return sb.ToString();
        }
        public void Initialize()
        {

        }

        public bool Gestured(PointInfo ActionPoint)
        {
            try
            {
                if (_Settings == null)
                    return false;

                // Win+L lockstation special handling
                if (_Settings.Windows &&
                  _Settings.KeyCode.Count != 0 && _Settings.KeyCode[0] == Keys.L)
                {
                    LockWorkStation();
                    return true;
                }

                // Check if safe mode is enabled in action settings
                Logging.LogDebug($"[HotKeyPlugin] Window: {ActionPoint.Window?.Title}, SendByKeybdEvent: {_Settings.SendByKeybdEvent}");

                if (_Settings.SendByKeybdEvent)
                {
                    Logging.LogDebug("[HotKeyPlugin] Using safe keyboard simulation (keybd_event)");
                    SendKeysSeparately(_Settings);
                }
                else
                {
                    Logging.LogDebug("[HotKeyPlugin] Using batch keyboard simulation (SendInput)");
                    SendShortcutKeys(_Settings);
                }
            }
            catch (Exception ex)
            {
                Logging.LogError($"[HotKeyPlugin] Error: {ex.Message}");
                var keyList = new List<Keys>();
                if (_Settings.Shift)
                    keyList.Add(Keys.LShiftKey);
                if (_Settings.Alt)
                    keyList.Add(Keys.LMenu);
                if (_Settings.Control)
                    keyList.Add(Keys.LControlKey);
                if (_Settings.Windows)
                    keyList.Add(Keys.LWin);

                keyList.AddRange(_Settings.KeyCode);

                KeyboardHelper.ResetKeyState(ActionPoint.Window, keyList.ToArray());
            }
            return true;
        }

        public bool Deserialize(string SerializedData)
        {
            Logging.LogDebug($"[HotKeyPlugin.Deserialize] Input data: {SerializedData}");
            bool result = PluginHelper.DeserializeSettings(SerializedData, out _Settings);
            Logging.LogDebug($"[HotKeyPlugin.Deserialize] Result: {result}, SendByKeybdEvent: {_Settings?.SendByKeybdEvent}");
            return result;
        }

        public string Serialize()
        {
            if (_GUI != null)
                _Settings = _GUI.Settings;

            if (_Settings == null)
                _Settings = new HotKeySettings();

            string serialized = PluginHelper.SerializeSettings(_Settings);
            Logging.LogDebug($"[HotKeyPlugin.Serialize] SendByKeybdEvent: {_Settings.SendByKeybdEvent}, Output: {serialized}");
            return serialized;
        }

        #endregion

        #region Private Methods

        private HotKey CreateGUI()
        {
            HotKey newGUI = new HotKey();

            newGUI.Loaded += (o, e) =>
            {
                TypedGUI.Settings = _Settings;
                TypedGUI.HostControl = HostControl;
            };

            return newGUI;
        }

        public static string GetDescription(HotKeySettings Settings)
        {
            if (Settings == null || Settings.KeyCode == null)
                return LocalizationProvider.Instance.GetTextValue("CorePlugins.HotKey.Description");

            // Create string to store key combination and final output description
            string strKeyCombo = "";
            string strFormattedOutput = LocalizationProvider.Instance.GetTextValue("CorePlugins.HotKey.SpecificDescription");

            // Build output string
            if (Settings.Windows)
                strKeyCombo = "Win + ";

            if (Settings.Control)
                strKeyCombo += "Ctrl + ";

            if (Settings.Alt)
                strKeyCombo += "Alt + ";

            if (Settings.Shift)
                strKeyCombo += "Shift + ";
            if (Settings.KeyCode.Count != 0)
            {
                foreach (var k in Settings.KeyCode)
                    strKeyCombo += GetKeyName(k) + " + ";
            }
            strKeyCombo = strKeyCombo.TrimEnd(' ', '+');

            // Return final formatted string
            return String.Format(strFormattedOutput, strKeyCombo);
        }

        private void SendKeysSeparately(HotKeySettings settings)
        {
            // Use keybd_event API (KeyboardKey) for safe mode - more reliable than SendInput
            // This ensures each key event is processed individually without batching

            KeyboardKey winKey = null;
            KeyboardKey controlKey = null;
            KeyboardKey altKey = null;
            KeyboardKey shiftKey = null;

            try
            {
                // Press modifier keys
                if (settings.Windows)
                {
                    winKey = new KeyboardKey(Keys.LWin);
                    winKey.Press();
                    System.Threading.Thread.Sleep(50);
                }

                if (settings.Control)
                {
                    controlKey = new KeyboardKey(Keys.LControlKey);
                    controlKey.Press();
                    System.Threading.Thread.Sleep(50);
                }

                if (settings.Alt)
                {
                    altKey = new KeyboardKey(Keys.LMenu);
                    altKey.Press();
                    System.Threading.Thread.Sleep(50);
                }

                if (settings.Shift)
                {
                    shiftKey = new KeyboardKey(Keys.LShiftKey);
                    shiftKey.Press();
                    System.Threading.Thread.Sleep(50);
                }

                // Press and release main keys
                if (settings.KeyCode != null)
                {
                    foreach (var k in settings.KeyCode)
                    {
                        KeyboardKey modifierKey = new KeyboardKey(k);
                        if (!String.IsNullOrEmpty(modifierKey.KeyName))
                        {
                            modifierKey.PressAndRelease();
                            System.Threading.Thread.Sleep(50);
                        }
                    }
                }

                // Release modifier keys in reverse order
                if (settings.Shift && shiftKey != null)
                {
                    shiftKey.Release();
                    System.Threading.Thread.Sleep(50);
                }

                if (settings.Alt && altKey != null)
                {
                    altKey.Release();
                    System.Threading.Thread.Sleep(50);
                }

                if (settings.Control && controlKey != null)
                {
                    controlKey.Release();
                    System.Threading.Thread.Sleep(50);
                }

                if (settings.Windows && winKey != null)
                {
                    winKey.Release();
                    System.Threading.Thread.Sleep(50);
                }
            }
            catch
            {
                // Ensure all keys are released on error
                shiftKey?.Release();
                altKey?.Release();
                controlKey?.Release();
                winKey?.Release();
                throw;
            }
        }

        private void SendShortcutKeys(HotKeySettings settings)
        {
            if (settings.SendByKeybdEvent)
            {

                // Create keyboard keys to represent hot key combinations
                KeyboardKey winKey = new KeyboardKey(Keys.LWin);
                KeyboardKey controlKey = new KeyboardKey(Keys.LControlKey);
                KeyboardKey altKey = new KeyboardKey(Keys.LMenu);
                KeyboardKey shiftKey = new KeyboardKey(Keys.LShiftKey);

                // Deceide which keys to press
                // Windows
                if (settings.Windows)
                    winKey.Press();

                // Control
                if (settings.Control)
                    controlKey.Press();

                // Alt
                if (settings.Alt)
                    altKey.Press();

                // Shift
                if (settings.Shift)
                    shiftKey.Press();

                // Modifier
                if (settings.KeyCode != null)
                    foreach (var k in settings.KeyCode)
                    {
                        KeyboardKey modifierKey = new KeyboardKey(k);
                        if (!String.IsNullOrEmpty(modifierKey.KeyName))
                            modifierKey.PressAndRelease();
                    }
                // Release Shift
                if (settings.Shift)
                    shiftKey.Release();

                // Release Alt
                if (settings.Alt)
                    altKey.Release();

                // Release Control
                if (settings.Control)
                    controlKey.Release();

                // Release Windows
                if (settings.Windows)
                    winKey.Release();
            }
            else
            {
                InputSimulator simulator = new InputSimulator();
                List<VirtualKeyCode> modifiedKeys = new List<VirtualKeyCode>();
                List<VirtualKeyCode> keys = new List<VirtualKeyCode>();

                if (settings.KeyCode != null)
                    foreach (var k in settings.KeyCode)
                    {
                        if (!Enum.IsDefined(typeof(VirtualKeyCode), k.GetHashCode())) continue;

                        var key = (VirtualKeyCode)k;
                        keys.Add(key);
                    }

                if (settings.Windows)
                    modifiedKeys.Add(VirtualKeyCode.LWIN);
                if (settings.Control)
                    modifiedKeys.Add(VirtualKeyCode.LCONTROL);
                if (settings.Alt)
                    modifiedKeys.Add(VirtualKeyCode.LMENU);
                if (settings.Shift)
                    modifiedKeys.Add(VirtualKeyCode.LSHIFT);

                if (modifiedKeys.Count == 0)
                {
                    if (keys.Count != 0)
                    {
                        simulator.Keyboard.KeyPress(keys.ToArray()).Sleep(30);
                    }
                }
                else
                {
                    if (keys.Count != 0)
                    {
                        simulator.Keyboard.ModifiedKeyStroke(modifiedKeys, keys).Sleep(30);
                    }
                    else
                    {
                        simulator.Keyboard.KeyPress(modifiedKeys.ToArray()).Sleep(30);
                    }
                }
            }
        }

        #endregion

        #region Host Control

        public IHostControl HostControl { get; set; }

        #endregion
    }
}