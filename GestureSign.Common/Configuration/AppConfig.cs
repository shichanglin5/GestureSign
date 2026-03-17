using GestureSign.Common.Input;
using GestureSign.Common.Log;
using GestureSign.Common.Applications;
using GestureSign.Common.Gestures;
using ManagedWinapi.Hooks;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Threading;

namespace GestureSign.Common.Configuration
{
    public class AppConfig
    {
        private static bool _loadFlag = true;
        private static Dictionary<string, object> _settingCache = new Dictionary<string, object>(16);
        static System.Configuration.Configuration _config;
        static Timer Timer;
        public static event EventHandler ConfigChanged;

        private static ExeConfigurationFileMap ExeMap;
        private static string _applicationDataPath;
        private static string _localApplicationDataPath;

        private static System.Configuration.Configuration Config
        {
            get
            {
                if (_config == null || _loadFlag)
                {
                    try
                    {
                        FileManager.WaitFile(ConfigPath);
                        _config = ConfigurationManager.OpenMappedExeConfiguration(ExeMap, ConfigurationUserLevel.None);
                        _settingCache.Clear();
                        _loadFlag = false;
                    }
                    catch (Exception e)
                    {
                        Logging.LogAndNotice(new Exceptions.FileWriteException(e));
                    }
                }
                return _config;
            }
        }

        public static string ApplicationDataPath
        {
            get
            {
                if (!Directory.Exists(_applicationDataPath))
                {
                    try
                    {
                        Directory.CreateDirectory(_applicationDataPath);
                    }
                    catch (Exception e)
                    {
                        Logging.LogAndNotice(new Exceptions.FileWriteException(e));
                    }
                }

                return _applicationDataPath;
            }
            private set => _applicationDataPath = value;
        }

        public static string LocalApplicationDataPath
        {
            get
            {
                if (!Directory.Exists(_localApplicationDataPath))
                {
                    try
                    {
                        Directory.CreateDirectory(_localApplicationDataPath);
                    }
                    catch (Exception e)
                    {
                        Logging.LogAndNotice(new Exceptions.FileWriteException(e));
                    }
                }

                return _localApplicationDataPath;
            }
            private set => _localApplicationDataPath = value;
        }

        public static string BackupPath { private set; get; }

        public static string ConfigPath { private set; get; }

        public static string CurrentFolderPath { private set; get; }

        #region Setting Parameters

        public static System.Drawing.Color VisualFeedbackColor
        {
            get
            {
                return (System.Drawing.Color)GetValue("VisualFeedbackColor", System.Drawing.Color.DeepSkyBlue);
            }
            set
            {
                SetValue("VisualFeedbackColor", value);
            }
        }

        public static int VisualFeedbackWidth
        {
            get
            {
                return (int)GetValue("VisualFeedbackWidth", 9);
            }
            set
            {
                SetValue("VisualFeedbackWidth", value);
            }
        }

        public static int MinimumFingerCountForVisualFeedback
        {
            get
            {
                return (int)GetValue("MinimumFingerCountForVisualFeedback", 1);
            }
            set
            {
                SetValue("MinimumFingerCountForVisualFeedback", value);
            }
        }

        public static int MinimumPointDistance
        {
            get
            {
                return (int)GetValue("MinimumPointDistance", 20);
            }
            set
            {
                SetValue("MinimumPointDistance", value);
            }
        }

        public static int TapDistanceThreshold
        {
            get
            {
                return (int)GetValue(nameof(TapDistanceThreshold), 50);
            }
            set
            {
                SetValue(nameof(TapDistanceThreshold), value);
            }
        }

        public static double Opacity
        {
            get
            {
                return (double)GetValue("Opacity", 0.35);
            }
            set
            {
                SetValue("Opacity", value);
            }
        }

        public static bool IsOrderByLocation
        {
            get
            {
                return (bool)GetValue("IsOrderByLocation", true);
            }
            set
            {
                SetValue("IsOrderByLocation", value);
            }
        }

        public static bool UiAccess { get; set; }
        public static bool ShowTrayIcon
        {
            get
            {
                return (bool)GetValue("ShowTrayIcon", true);
            }
            set
            {
                SetValue("ShowTrayIcon", value);
            }
        }

        public static string CultureName
        {
            get
            {
                return (string)GetValue("CultureName", "");
            }
            set
            {
                SetValue("CultureName", value);
            }
        }

        public static bool SendErrorReport
        {
            get
            {
                return (bool)GetValue("SendErrorReport", true);
            }
            set
            {
                SetValue("SendErrorReport", value);
            }
        }

        public static DateTime LastErrorTime
        {
            get
            {
                return GetValue("LastErrorTime", DateTime.MinValue);
            }
            set
            {
                SetValue("LastErrorTime", value);
            }
        }

        public static int InitialTimeout
        {
            get
            {
                return (int)GetValue(nameof(InitialTimeout), 0);
            }
            set
            {
                SetValue(nameof(InitialTimeout), value);
            }
        }

        public static Log.LogLevel LogLevel
        {
            get
            {
                return (Log.LogLevel)GetValue(nameof(LogLevel), (int)Log.LogLevel.Info);
            }
            set
            {
                SetValue(nameof(LogLevel), (int)value);
                // Update current log level in Logging class
                Log.Logging.CurrentLogLevel = value;
            }
        }

        public static bool RegisterTouchPad
        {
            get
            {
                return (bool)GetValue(nameof(RegisterTouchPad), true);
            }
            set
            {
                SetValue(nameof(RegisterTouchPad), value);
            }
        }

        public static bool RegisterTouchScreen
        {
            get
            {
                return GetValue(nameof(RegisterTouchScreen), true);
            }
            set
            {
                SetValue(nameof(RegisterTouchScreen), value);
            }
        }

        public static bool IgnoreFullScreen
        {
            get
            {
                return GetValue(nameof(IgnoreFullScreen), false);
            }
            set
            {
                SetValue(nameof(IgnoreFullScreen), value);
            }
        }

        public static bool IsLeftHanded
        {
            get
            {
                return GetValue(nameof(IsLeftHanded), false);
            }
            set
            {
                SetValue(nameof(IsLeftHanded), value);
            }
        }

        public static bool RunAsAdmin
        {
            get
            {
                return GetValue(nameof(RunAsAdmin), false);
            }
            set
            {
                SetValue(nameof(RunAsAdmin), value);
            }
        }

        public static bool DrawFeatureFingerOnly
        {
            get
            {
                return GetValue(nameof(DrawFeatureFingerOnly), false);
            }
            set
            {
                SetValue(nameof(DrawFeatureFingerOnly), value);
            }
        }

        public static bool BlockWindowsGestures
        {
            get
            {
                return GetValue(nameof(BlockWindowsGestures), false);
            }
            set
            {
                SetValue(nameof(BlockWindowsGestures), value);
            }
        }

        public static int GestureMatchProbability
        {
            get
            {
                return (int)GetValue(nameof(GestureMatchProbability), 80);
            }
            set
            {
                SetValue(nameof(GestureMatchProbability), value);
            }
        }

        public static FingerMatchStrategy TrajectoryMatchStrategy
        {
            get
            {
                var raw = (int)GetValue(nameof(TrajectoryMatchStrategy), (int)FingerMatchStrategy.AllFingers);
                return Enum.IsDefined(typeof(FingerMatchStrategy), raw)
                    ? (FingerMatchStrategy)raw
                    : FingerMatchStrategy.AllFingers;
            }
            set
            {
                SetValue(nameof(TrajectoryMatchStrategy), (int)value);
            }
        }


        public static int MultiFingerDelay
        {
            get
            {
                return (int)GetValue(nameof(MultiFingerDelay), 100);
            }
            set
            {
                SetValue(nameof(MultiFingerDelay), value);
            }
        }

        public static int TapMaxDurationMs
        {
            get
            {
                return (int)GetValue(nameof(TapMaxDurationMs), 300);
            }
            set
            {
                SetValue(nameof(TapMaxDurationMs), value);
            }
        }

        public static int ClickMaxPressDurationMs
        {
            get
            {
                return (int)GetValue(nameof(ClickMaxPressDurationMs), 600);
            }
            set
            {
                SetValue(nameof(ClickMaxPressDurationMs), value);
            }
        }

        public static int ClickMaxMovementPx
        {
            get
            {
                return (int)GetValue(nameof(ClickMaxMovementPx), 50);
            }
            set
            {
                SetValue(nameof(ClickMaxMovementPx), value);
            }
        }

        public static int TipTapMaxTapDurationMs
        {
            get
            {
                return (int)GetValue(nameof(TipTapMaxTapDurationMs), 150);
            }
            set
            {
                SetValue(nameof(TipTapMaxTapDurationMs), value);
            }
        }

        public static int TipTapFixMinHoldMs
        {
            get
            {
                return (int)GetValue(nameof(TipTapFixMinHoldMs), 50);
            }
            set
            {
                SetValue(nameof(TipTapFixMinHoldMs), value);
            }
        }

        public static double FingerMovementJitterThresholdPx
        {
            get
            {
                return (double)GetValue(nameof(FingerMovementJitterThresholdPx), 3.0);
            }
            set
            {
                SetValue(nameof(FingerMovementJitterThresholdPx), value);
            }
        }

        public static Input.WindowTargetMode TouchPadWindowTargetMode
        {
            get
            {
                return (Input.WindowTargetMode)GetValue(nameof(TouchPadWindowTargetMode), (int)Input.WindowTargetMode.MousePosition);
            }
            set
            {
                SetValue(nameof(TouchPadWindowTargetMode), (int)value);
            }
        }

        public static Input.WindowTargetMode TouchScreenWindowTargetMode
        {
            get
            {
                return (Input.WindowTargetMode)GetValue(nameof(TouchScreenWindowTargetMode), (int)Input.WindowTargetMode.GestureStartPosition);
            }
            set
            {
                SetValue(nameof(TouchScreenWindowTargetMode), (int)value);
            }
        }

        /// <summary>
        /// 全局默认窗口激活方式。
        /// 1 = AttachThreadInput（兼容模式，适用于少数顽固窗口），
        /// 2 = SafeMode（默认；ActivateApp 会优先走安全路径，并按目标窗口记忆是否需要升级到兼容模式）
        /// </summary>
        public static int DefaultActivationMethod
        {
            get
            {
                return GetValue(nameof(DefaultActivationMethod), 2);
            }
            set
            {
                SetValue(nameof(DefaultActivationMethod), value);
            }
        }

        public static int NormalizeDefaultActivationMethod(int value)
        {
            return value == (int)ActivationMethod.AttachThreadInput ||
                   value == (int)ActivationMethod.SafeMode
                ? value
                : (int)ActivationMethod.SafeMode;
        }

        public static bool ReFetchTargetWindowOnExecution
        {
            get
            {
                return GetValue(nameof(ReFetchTargetWindowOnExecution), false);
            }
            set
            {
                SetValue(nameof(ReFetchTargetWindowOnExecution), value);
            }
        }

        #endregion

        static AppConfig()
        {
#if uiAccess
            UiAccess = VersionHelper.IsWindows8OrGreater();
#endif
            CurrentFolderPath = Path.GetDirectoryName(new Uri(System.Reflection.Assembly.GetExecutingAssembly().CodeBase).LocalPath);
#if Portable
            ApplicationDataPath = Path.Combine(CurrentFolderPath, "AppData");
            LocalApplicationDataPath = ApplicationDataPath;

            ConfigPath = Path.Combine(ApplicationDataPath, Constants.ConfigFileName);
            BackupPath = Path.Combine(LocalApplicationDataPath, "Backup");
#else
            ApplicationDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GestureSign");
            LocalApplicationDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GestureSign");

            ConfigPath = Path.Combine(ApplicationDataPath, Constants.ConfigFileName);
            BackupPath = LocalApplicationDataPath + "\\Backup";

#endif
            ExeMap = new ExeConfigurationFileMap
            {
                ExeConfigFilename = ConfigPath,
                RoamingUserConfigFilename = ConfigPath,
            };
            Timer = new Timer(SaveFile, null, Timeout.Infinite, Timeout.Infinite);
        }

        public static void Reload()
        {
            _loadFlag = true;
            _settingCache.Clear();
            if (ConfigChanged != null)
                ConfigChanged(new object(), EventArgs.Empty);
        }


        private static void Save()
        {
            Timer.Change(100, Timeout.Infinite);
        }

        private static void SaveFile(object state)
        {
            try
            {
                FileManager.WaitFile(ConfigPath);
                // Save the configuration file.
                var config = Config;
                config.AppSettings.SectionInformation.ForceSave = true;
                config.Save(ConfigurationSaveMode.Modified);
            }
            catch (ConfigurationErrorsException e)
            {
                Reload();
                Logging.LogAndNotice(new Exceptions.FileWriteException(e));
            }
            catch (Exception e)
            {
                Logging.LogAndNotice(e);
            }
            // Force a reload of the changed section.
            ConfigurationManager.RefreshSection("appSettings");
            ConfigChanged?.Invoke(new object(), EventArgs.Empty);
        }

        private static T GetValue<T>(string key, T defaultValue, Func<string, T> converter)
        {
            if (Config == null)
                return defaultValue;
            var setting = Config.AppSettings.Settings[key];
            if (setting != null)
            {
                try
                {
                    return converter(setting.Value);
                }
                catch
                {
                    Config.AppSettings.Settings.Remove(key);
                    return defaultValue;
                }
            }
            return defaultValue;
        }

        private static T GetCacheValue<T>(string key, T defaultValue, Func<string, T> converter)
        {
            object output;
            if (_settingCache.TryGetValue(key, out output))
            {
                return (T)output;
            }
            else
            {
                var value = GetValue(key, defaultValue, converter);
                _settingCache.Add(key, value);
                return value;
            }
        }

        private static int GetValue(string key, int defaultValue)
        {
            return GetCacheValue(key, defaultValue, s => int.Parse(s));
        }

        private static double GetValue(string key, double defaultValue)
        {
            return GetCacheValue(key, defaultValue, s => double.Parse(s));
        }

        private static bool GetValue(string key, bool defaultValue)
        {
            return GetCacheValue(key, defaultValue, s => bool.Parse(s));
        }

        private static string GetValue(string key, string defaultValue)
        {
            return GetValue(key, defaultValue, s => s);
        }

        private static DateTime GetValue(string key, DateTime defaultValue)
        {
            string setting = GetValue(key, string.Empty);
            if (!string.IsNullOrEmpty(setting))
            {
                try
                {
                    return DateTime.Parse(setting);
                }
                catch
                {
                    Config.AppSettings.Settings.Remove(key);
                    return defaultValue;
                }
            }
            else return defaultValue;
        }

        private static System.Drawing.Color GetValue(string key, System.Drawing.Color defaultValue)
        {
            string setting = GetValue(key, string.Empty);
            if (!string.IsNullOrEmpty(setting))
            {
                try
                {
                    return GetCacheValue(key, defaultValue, System.Drawing.ColorTranslator.FromHtml);
                }
                catch
                {
                    Config.AppSettings.Settings.Remove(key);
                    return defaultValue;
                }
            }
            else
            {
                System.Drawing.Color color;
                if (GetWindowGlassColor(out color))
                    return color;
                return defaultValue;
            }
        }

        private static void SetValue<T>(string key, T value)
        {
            if (Config == null)
                return;
            _settingCache.Clear();

            if (Config.AppSettings.Settings[key] != null)
            {
                Config.AppSettings.Settings[key].Value = value.ToString();
            }
            else
            {
                Config.AppSettings.Settings.Add(key, value.ToString());
            }
            Save();
        }

        private static void SetValue(string key, System.Drawing.Color value)
        {
            SetValue(key, System.Drawing.ColorTranslator.ToHtml(value));
        }

        private static void SetValue(string key, DateTime value)
        {
            SetValue(key, value.ToString(CultureInfo.InvariantCulture));
        }

        private static bool GetWindowGlassColor(out System.Drawing.Color windowGlassColor)
        {
            windowGlassColor = System.Drawing.Color.Empty;
            try
            {
                if (VersionHelper.IsWindowsVistaOrGreater())
                {
                    using (RegistryKey dwm = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                    {
                        if (dwm == null)
                            return false;
                        var colorizationColor = dwm.GetValue("ColorizationColor");
                        if (colorizationColor == null)
                            return false;

                        windowGlassColor = System.Drawing.Color.FromArgb((int)colorizationColor | -16777216);
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }
            return false;
        }
    }
}
