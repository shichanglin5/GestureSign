using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
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
                _ => false
            };
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
                return windowTitle.Contains(pattern, StringComparison.OrdinalIgnoreCase);
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
                _ => false
            };
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
                return windowTitle.Contains(pattern, StringComparison.OrdinalIgnoreCase);
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
