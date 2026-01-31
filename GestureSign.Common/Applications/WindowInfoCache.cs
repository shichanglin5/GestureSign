using ManagedWinapi.Windows;
using System;
using System.IO;

namespace GestureSign.Common.Applications
{
    /// <summary>
    /// 窗口信息缓存，支持按需获取和缓存属性
    /// 注意：EmptyMarker 仅用于内部标记"已获取但为空"，不会与配置值比较
    /// </summary>
    internal class WindowInfoCache
    {
        private readonly SystemWindow _window;
        private readonly IntPtr _hWnd;

        // 缓存的属性（null = 未获取，EmptyMarker = 已获取但为空）
        private string _className;
        private string _title;
        private string _processName;
        private string _processPath;
        private string _aumid;

        private const string EmptyMarker = "\0EMPTY\0";  // 内部标记，不会与配置值匹配

        public WindowInfoCache(SystemWindow window)
        {
            _window = window;
            _hWnd = window.HWnd;
        }

        public string GetClassName()
        {
            if (_className == null)
            {
                try { _className = _window.ClassName ?? EmptyMarker; }
                catch { _className = EmptyMarker; }
            }
            return _className == EmptyMarker ? null : _className;
        }

        public string GetTitle()
        {
            if (_title == null)
            {
                try { _title = _window.Title ?? EmptyMarker; }
                catch { _title = EmptyMarker; }
            }
            return _title == EmptyMarker ? null : _title;
        }

        public string GetProcessName()
        {
            if (_processName == null)
            {
                try
                {
                    var path = _window.GetProcessFilePath();
                    _processName = string.IsNullOrEmpty(path) ? EmptyMarker : Path.GetFileNameWithoutExtension(path);
                }
                catch { _processName = EmptyMarker; }
            }
            return _processName == EmptyMarker ? null : _processName;
        }

        public string GetProcessPath()
        {
            if (_processPath == null)
            {
                try { _processPath = _window.GetProcessFilePath() ?? EmptyMarker; }
                catch { _processPath = EmptyMarker; }
            }
            return _processPath == EmptyMarker ? null : _processPath;
        }

        public string GetAUMID()
        {
            if (_aumid == null)
            {
                try { _aumid = WindowMatcher.GetWindowAUMID(_hWnd) ?? EmptyMarker; }
                catch { _aumid = EmptyMarker; }
            }
            return _aumid == EmptyMarker ? null : _aumid;
        }
    }
}
