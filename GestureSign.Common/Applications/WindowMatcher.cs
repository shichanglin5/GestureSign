using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;
using GestureSign.Common.Log;
using ManagedWinapi.Windows;

#pragma warning disable CA1416 // Platform-specific API

namespace GestureSign.Common.Applications
{
    /// <summary>
    /// 统一窗口匹配逻辑
    /// </summary>
    public static class WindowMatcher
    {
        #region PInvoke Declarations

        [DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        private const uint GA_ROOT = 2;
        private const uint GA_ROOTOWNER = 3;

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, out IPropertyStore propertyStore);

        [ComImport]
        [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            [PreserveSig]
            int GetCount(out uint count);
            [PreserveSig]
            int GetAt(uint iProp, out PropertyKey pkey);
            [PreserveSig]
            int GetValue(ref PropertyKey key, out PropVariant pv);
            [PreserveSig]
            int SetValue(ref PropertyKey key, ref PropVariant pv);
            [PreserveSig]
            int Commit();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey
        {
            public Guid fmtid;
            public uint pid;

            public PropertyKey(Guid fmtid, uint pid)
            {
                this.fmtid = fmtid;
                this.pid = pid;
            }
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct PropVariant
        {
            [FieldOffset(0)] public ushort vt;
            [FieldOffset(8)] public IntPtr pwszVal;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GUITHREADINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public int rcCaretLeft;
            public int rcCaretTop;
            public int rcCaretRight;
            public int rcCaretBottom;
        }

        [DllImport("user32.dll")]
        private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        #endregion

        #region Cache

        /// <summary>
        /// 窗口信息缓存（键为窗口句柄）
        /// </summary>
        private class WindowInfoCache
        {
            public string ClassName { get; set; }
            public string Title { get; set; }
            public string ProcessName { get; set; }
            public string ProcessPath { get; set; }
            public string AUMID { get; set; }
            public DateTime CachedAt { get; set; }
        }

        private static readonly ConcurrentDictionary<IntPtr, WindowInfoCache> _cache = new();
        private static readonly TimeSpan CacheExpiration = TimeSpan.FromSeconds(5);
        private static Timer _cleanupTimer;

        static WindowMatcher()
        {
            // 每 30 秒清理一次过期缓存
            _cleanupTimer = new Timer(CleanupCache, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        private static void CleanupCache(object state)
        {
            var now = DateTime.UtcNow;
            var expiredKeys = _cache.Where(kvp => now - kvp.Value.CachedAt > CacheExpiration)
                                    .Select(kvp => kvp.Key)
                                    .ToList();
            foreach (var key in expiredKeys)
            {
                _cache.TryRemove(key, out _);
            }
        }

        /// <summary>
        /// 清空缓存
        /// </summary>
        public static void ClearCache()
        {
            _cache.Clear();
        }

        /// <summary>
        /// 获取或创建窗口信息缓存
        /// </summary>
        private static WindowInfoCache GetOrCreateCache(IntPtr hWnd, SystemWindow window)
        {
            if (_cache.TryGetValue(hWnd, out var cached))
            {
                if (DateTime.UtcNow - cached.CachedAt < CacheExpiration)
                    return cached;
            }

            var info = new WindowInfoCache
            {
                ClassName = window?.ClassName,
                Title = window?.Title,
                CachedAt = DateTime.UtcNow
            };

            _cache[hWnd] = info;
            return info;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 获取窗口的 AUMID（应用用户模型 ID），带缓存
        /// </summary>
        public static string GetWindowAUMID(IntPtr hWnd)
        {
            return GetWindowAUMID(hWnd, out _);
        }

        /// <summary>
        /// 获取窗口的 AUMID（应用用户模型 ID），带缓存，并返回是否命中缓存
        /// </summary>
        public static string GetWindowAUMID(IntPtr hWnd, out bool cacheHit)
        {
            // 先检查缓存
            if (_cache.TryGetValue(hWnd, out var cached) && cached.AUMID != null)
            {
                cacheHit = true;
                return cached.AUMID == string.Empty ? null : cached.AUMID;
            }

            cacheHit = false;
            var aumid = GetWindowAUMIDInternal(hWnd);

            // 更新或创建缓存（用空字符串表示已查询但无 AUMID）
            var cacheEntry = _cache.GetOrAdd(hWnd, _ => new WindowInfoCache { CachedAt = DateTime.UtcNow });
            cacheEntry.AUMID = aumid ?? string.Empty;

            return aumid;
        }

        /// <summary>
        /// 获取窗口进程的完整路径（带缓存和访问被拒回退逻辑）
        /// </summary>
        public static string GetProcessPath(IntPtr hWnd, out string processName)
        {
            return GetProcessPath(hWnd, out processName, out _);
        }

        /// <summary>
        /// 获取窗口进程的完整路径（带缓存和访问被拒回退逻辑），并返回是否命中缓存
        /// </summary>
        public static string GetProcessPath(IntPtr hWnd, out string processName, out bool cacheHit)
        {
            // 先检查缓存
            if (_cache.TryGetValue(hWnd, out var cached))
            {
                if (cached.ProcessPath != null || cached.ProcessName != null)
                {
                    cacheHit = true;
                    processName = cached.ProcessName;
                    return cached.ProcessPath == string.Empty ? null : cached.ProcessPath;
                }
            }

            cacheHit = false;
            var result = GetProcessPathInternal(hWnd, out processName);

            // 更新或创建缓存
            var cacheEntry = _cache.GetOrAdd(hWnd, _ => new WindowInfoCache { CachedAt = DateTime.UtcNow });
            cacheEntry.ProcessPath = result ?? string.Empty;
            cacheEntry.ProcessName = processName;

            return result;
        }

        /// <summary>
        /// 获取窗口进程的完整路径
        /// </summary>
        public static string GetProcessPath(IntPtr hWnd)
        {
            return GetProcessPath(hWnd, out _);
        }

        /// <summary>
        /// 匹配单个条件（供外部插件调用）
        /// </summary>
        public static bool MatchSingleCondition(SystemWindow window, MatchCondition condition)
        {
            if (window == null || condition == null || string.IsNullOrEmpty(condition.Value))
                return false;

            var hWnd = window.HWnd;
            var cache = GetOrCreateCache(hWnd, window);
            return MatchCondition(window, hWnd, cache, condition);
        }

        /// <summary>
        /// 匹配所有条件（AND 逻辑，供外部插件调用）
        /// </summary>
        public static bool MatchAllConditions(SystemWindow window, List<MatchCondition> conditions)
        {
            if (window == null)
                return false;

            if (conditions == null || conditions.Count == 0)
                return false;  // 插件必须有条件

            try
            {
                var hWnd = window.HWnd;
                var cache = GetOrCreateCache(hWnd, window);

                // 按性能优先级排序条件，快速失败
                var sortedConditions = conditions.OrderBy(c => c.GetPriority());

                foreach (var condition in sortedConditions)
                {
                    if (!MatchCondition(window, hWnd, cache, condition))
                        return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Logging.LogTrace($"[WindowMatcher] MatchAllConditions error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 匹配所有条件（AND 逻辑，使用外部 WindowInfoCache）
        /// </summary>
        public static bool MatchAllConditions(Applications.WindowInfoCache windowInfo, List<MatchCondition> conditions)
        {
            if (windowInfo == null)
                return false;

            if (conditions == null || conditions.Count == 0)
                return false;

            try
            {
                foreach (var condition in conditions)
                {
                    if (string.IsNullOrEmpty(condition.Value))
                        continue;

                    if (!MatchConditionWithCache(windowInfo, condition))
                        return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Logging.LogTrace($"[WindowMatcher] MatchAllConditions error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 匹配单个条件（使用外部 WindowInfoCache）
        /// </summary>
        private static bool MatchConditionWithCache(Applications.WindowInfoCache windowInfo, MatchCondition condition)
        {
            return condition.Type switch
            {
                MatchConditionType.ClassName =>
                    MatchClassName(windowInfo.GetClassName(), condition.Value),
                MatchConditionType.Title =>
                    MatchTitleValue(windowInfo.GetTitle(), condition.Value, condition.IsRegex),
                MatchConditionType.ProcessName =>
                    MatchProcessNameValue(windowInfo.GetProcessName(), condition.Value),
                MatchConditionType.ProcessPath =>
                    MatchProcessPathValue(windowInfo.GetProcessPath(), windowInfo.GetProcessName(), condition.Value),
                MatchConditionType.AUMID =>
                    MatchValue(windowInfo.GetAUMID(), condition.Value),
                MatchConditionType.FocusedTextInput =>
                    MatchFocusedTextInput(windowInfo, condition.Value),
                _ => false
            };
        }

        /// <summary>
        /// 解析 FocusedTextInput 的 Value 并匹配
        /// </summary>
        private static bool MatchFocusedTextInput(Applications.WindowInfoCache windowInfo, string value)
        {
            ParseFocusedTextInputValue(value, out bool expected, out bool useUIA);
            bool isTextInput = windowInfo.GetIsFocusedTextInput(useUIA);
            return isTextInput == expected;
        }

        /// <summary>
        /// 解析 FocusedTextInput 的配置值
        /// </summary>
        internal static void ParseFocusedTextInputValue(string value, out bool expected, out bool useUIA)
        {
            useUIA = false;
            expected = true;

            if (string.IsNullOrEmpty(value))
                return;

            var parts = value.Split(',');
            expected = string.Equals(parts[0].Trim(), "true", StringComparison.OrdinalIgnoreCase);
            useUIA = parts.Length > 1 && string.Equals(parts[1].Trim(), "uia", StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchValue(string actualValue, string expectedValue)
        {
            if (string.IsNullOrEmpty(actualValue))
                return false;
            return string.Equals(actualValue, expectedValue, StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchTitleValue(string windowTitle, string pattern, bool isRegex)
        {
            if (string.IsNullOrEmpty(windowTitle))
                return false;

            if (isRegex)
            {
                try
                {
                    return Regex.IsMatch(windowTitle, pattern, RegexOptions.IgnoreCase);
                }
                catch
                {
                    return false;
                }
            }
            else
            {
                return string.Equals(windowTitle, pattern, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static bool MatchProcessNameValue(string actualProcessName, string expectedProcessName)
        {
            if (string.IsNullOrEmpty(actualProcessName))
                return false;

            var expected = expectedProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(expectedProcessName)
                : expectedProcessName;

            return string.Equals(actualProcessName, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchProcessPathValue(string actualPath, string processName, string expectedPath)
        {
            // 优先完整路径匹配
            if (!string.IsNullOrEmpty(actualPath))
            {
                return string.Equals(actualPath, expectedPath, StringComparison.OrdinalIgnoreCase);
            }

            // 回退到进程名匹配
            if (!string.IsNullOrEmpty(processName))
            {
                string expectedFileName = Path.GetFileName(expectedPath);
                string actualFileName = processName + ".exe";
                return string.Equals(expectedFileName, actualFileName, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        #endregion

        #region Focused Text Input Detection

        /// <summary>
        /// 已知的文本输入框类名（不区分大小写）
        /// </summary>
        private static readonly HashSet<string> TextInputClassNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "Edit",                // 标准 Win32 Edit 控件
                "RichEdit20W",         // RichEdit 2.0
                "RichEdit50W",         // RichEdit 5.0 (MS Office)
                "RICHEDIT60W",         // RichEdit 6.0
                "RichEditD2DPT",       // DirectWrite RichEdit
                "TextBox",             // WinForms TextBox
                "RichTextBox",         // WinForms RichTextBox
                "TextBoxView",         // WPF TextBox 内部
                "_WwG",                // MS Word 编辑区
                "Scintilla",           // Scintilla 编辑器控件
                "ConsoleWindowClass",  // 控制台窗口
            };

        /// <summary>
        /// 检测指定窗口线程的焦点控件是否为文本输入框（三级检测）
        /// </summary>
        /// <param name="hWnd">目标窗口句柄</param>
        /// <param name="useUIA">是否启用 UI Automation 回退检测</param>
        public static bool DetectFocusedTextInput(IntPtr hWnd, bool useUIA = false)
        {
            try
            {
                GetWindowThreadProcessId(hWnd, out int pid);
                if (pid == 0) return false;

                var info = new GUITHREADINFO();
                info.cbSize = Marshal.SizeOf(info);

                // 获取目标窗口所在线程的 GUI 信息
                uint threadId = (uint)GetWindowThreadProcessId(hWnd, out _);
                if (!GetGUIThreadInfo(threadId, ref info))
                    return useUIA && DetectTextInputViaUIA();

                // 第一级：ClassName + Caret 检测（~2μs）
                if (info.hwndFocus != IntPtr.Zero)
                {
                    string? focusedClassName = GetWindowClassNameSafe(info.hwndFocus);
                    if (!string.IsNullOrEmpty(focusedClassName) && IsTextInputClassName(focusedClassName))
                        return true;

                    // Caret 辅助判据：光标必须在焦点窗口上才可信
                    // 避免 Chrome/Electron 等应用的隐藏 IME 光标导致误报
                    if (info.hwndCaret != IntPtr.Zero && info.hwndCaret == info.hwndFocus)
                        return true;
                }

                // 第三级：UIA 回退（~1-50ms，仅当启用时）
                if (useUIA)
                    return DetectTextInputViaUIA();

                return false;
            }
            catch (Exception ex)
            {
                Logging.LogTrace($"[WindowMatcher] DetectFocusedTextInput error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取窗口类名（安全版本，不抛异常）
        /// </summary>
        private static string? GetWindowClassNameSafe(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            int result = GetClassName(hWnd, sb, sb.Capacity);
            return result > 0 ? sb.ToString() : null;
        }

        private static bool IsTextInputClassName(string className)
        {
            if (TextInputClassNames.Contains(className))
                return true;

            // 前缀匹配：RichEdit 系列可能有版本变体
            if (className.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        /// <summary>
        /// 通过 UI Automation 检测焦点元素是否为文本输入框
        /// </summary>
        private static bool DetectTextInputViaUIA()
        {
            try
            {
                var focusedElement = AutomationElement.FocusedElement;
                if (focusedElement == null)
                    return false;

                var controlType = focusedElement.Current.ControlType;

                // ControlType.Edit 是文本输入框的标准类型
                if (controlType == ControlType.Edit)
                    return true;

                // ControlType.Document 且支持编辑
                if (controlType == ControlType.Document)
                {
                    if ((bool)focusedElement.GetCurrentPropertyValue(
                        AutomationElement.IsValuePatternAvailableProperty))
                        return true;

                    if ((bool)focusedElement.GetCurrentPropertyValue(
                        AutomationElement.IsTextPatternAvailableProperty))
                        return true;
                }

                // 自定义控件：可键盘聚焦 + ValuePattern + 类名含 text/edit
                if (controlType == ControlType.Custom)
                {
                    bool isKeyboardFocusable = focusedElement.Current.IsKeyboardFocusable;
                    bool hasValuePattern = (bool)focusedElement.GetCurrentPropertyValue(
                        AutomationElement.IsValuePatternAvailableProperty);
                    if (isKeyboardFocusable && hasValuePattern)
                    {
                        string automationClassName = focusedElement.Current.ClassName ?? "";
                        if (automationClassName.Contains("text", StringComparison.OrdinalIgnoreCase) ||
                            automationClassName.Contains("edit", StringComparison.OrdinalIgnoreCase) ||
                            (bool)focusedElement.GetCurrentPropertyValue(
                                AutomationElement.IsTextPatternAvailableProperty))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Logging.LogTrace($"[WindowMatcher] DetectTextInputViaUIA error: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 匹配单个条件
        /// </summary>
        private static bool MatchCondition(SystemWindow window, IntPtr hWnd, WindowInfoCache cache, MatchCondition condition)
        {
            if (string.IsNullOrEmpty(condition.Value))
                return false;

            return condition.Type switch
            {
                MatchConditionType.ClassName => MatchClassName(cache.ClassName ?? window.ClassName, condition.Value),
                MatchConditionType.Title => MatchTitle(cache.Title ?? window.Title, condition.Value, condition.IsRegex),
                MatchConditionType.ProcessName => MatchProcessName(hWnd, cache, condition.Value),
                MatchConditionType.ProcessPath => MatchProcessPath(hWnd, cache, condition.Value),
                MatchConditionType.AUMID => MatchAUMID(hWnd, condition.Value),
                MatchConditionType.FocusedTextInput => MatchFocusedTextInputDirect(hWnd, condition.Value),
                _ => false
            };
        }

        private static bool MatchFocusedTextInputDirect(IntPtr hWnd, string value)
        {
            ParseFocusedTextInputValue(value, out bool expected, out bool useUIA);
            bool isTextInput = DetectFocusedTextInput(hWnd, useUIA);
            return isTextInput == expected;
        }

        private static bool MatchClassName(string windowClassName, string expectedClassName)
        {
            if (string.IsNullOrEmpty(windowClassName))
                return false;

            return string.Equals(windowClassName, expectedClassName, StringComparison.Ordinal);
        }

        private static bool MatchTitle(string windowTitle, string pattern, bool isRegex)
        {
            if (string.IsNullOrEmpty(windowTitle))
                return false;

            if (isRegex)
            {
                try
                {
                    return Regex.IsMatch(windowTitle, pattern, RegexOptions.IgnoreCase);
                }
                catch
                {
                    return false;
                }
            }
            else
            {
                return string.Equals(windowTitle, pattern, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static bool MatchProcessName(IntPtr hWnd, WindowInfoCache cache, string expectedProcessName)
        {
            // 确保缓存中有进程名
            if (cache.ProcessName == null)
            {
                GetProcessPath(hWnd, out _);
            }

            if (string.IsNullOrEmpty(cache.ProcessName))
                return false;

            // 支持带或不带 .exe 后缀的匹配
            var expected = expectedProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(expectedProcessName)
                : expectedProcessName;

            return string.Equals(cache.ProcessName, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchProcessPath(IntPtr hWnd, WindowInfoCache cache, string expectedPath)
        {
            // 确保缓存中有进程路径
            if (cache.ProcessPath == null && cache.ProcessName == null)
            {
                GetProcessPath(hWnd, out _);
            }

            // 优先完整路径匹配
            if (!string.IsNullOrEmpty(cache.ProcessPath) && cache.ProcessPath != string.Empty)
            {
                return string.Equals(cache.ProcessPath, expectedPath, StringComparison.OrdinalIgnoreCase);
            }

            // 回退到进程名匹配（访问被拒时）
            if (!string.IsNullOrEmpty(cache.ProcessName))
            {
                string expectedFileName = Path.GetFileName(expectedPath);
                string actualFileName = cache.ProcessName + ".exe";
                return string.Equals(expectedFileName, actualFileName, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private static bool MatchAUMID(IntPtr hWnd, string expectedAUMID)
        {
            // 先获取根窗口句柄
            IntPtr rootWnd = GetAncestor(hWnd, GA_ROOT);
            bool isRootWindow = (rootWnd == IntPtr.Zero || rootWnd == hWnd);

            IntPtr targetWnd = isRootWindow ? hWnd : rootWnd;
            string windowAUMID = GetWindowAUMID(targetWnd);

            // 如果根窗口没有 AUMID，尝试根所有者窗口
            if (string.IsNullOrEmpty(windowAUMID) && !isRootWindow)
            {
                IntPtr rootOwner = GetAncestor(hWnd, GA_ROOTOWNER);
                if (rootOwner != IntPtr.Zero && rootOwner != rootWnd)
                {
                    windowAUMID = GetWindowAUMID(rootOwner);
                }
            }

            if (string.IsNullOrEmpty(windowAUMID))
                return false;

            return string.Equals(windowAUMID, expectedAUMID, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetWindowAUMIDInternal(IntPtr hWnd)
        {
            try
            {
                Guid iid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
                PropertyKey PKEY_AppUserModel_ID = new PropertyKey(
                    new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

                int result = SHGetPropertyStoreForWindow(hWnd, ref iid, out IPropertyStore propertyStore);
                if (result != 0)
                    return null;

                try
                {
                    result = propertyStore.GetValue(ref PKEY_AppUserModel_ID, out PropVariant pv);
                    if (result != 0 || pv.vt != 31)
                        return null;

                    string aumid = Marshal.PtrToStringUni(pv.pwszVal);
                    return string.IsNullOrEmpty(aumid) ? null : aumid;
                }
                finally
                {
                    Marshal.ReleaseComObject(propertyStore);
                }
            }
            catch
            {
                return null;
            }
        }

        private static string GetProcessPathInternal(IntPtr hWnd, out string processName)
        {
            processName = null;
            try
            {
                GetWindowThreadProcessId(hWnd, out int pid);
                var process = Process.GetProcessById(pid);
                processName = process.ProcessName;

                try
                {
                    return process.MainModule?.FileName;
                }
                catch (Win32Exception)
                {
                    // 访问被拒，返回 null 但保留 processName 用于回退匹配
                    Logging.LogTrace($"[WindowMatcher] Process path access denied for PID {pid}, fallback to process name: {processName}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Logging.LogTrace($"[WindowMatcher] Failed to get process info: {ex.Message}");
                return null;
            }
        }

        #endregion
    }
}
