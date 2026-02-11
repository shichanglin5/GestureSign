using GestureSign.Common.Configuration;
using GestureSign.Daemon.Native;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GestureSign.Daemon.Filtration
{
    public class PointerInputTargetWindow : Form
    {
        private bool _isRegistered;
        private int _blockTouchInputThreshold;
        private Dictionary<int, int> _pointerIdList = new Dictionary<int, int>(10);
        private Queue<int> _idPool = new Queue<int>(10);
        private HashSet<int> _blockedPointerIds = new HashSet<int>();
        private bool _isInitialized = false;
        private bool _tempDisable;
        private int _lastFrameID;

        // 延迟注入相关
        private List<POINTER_TOUCH_INFO> _pendingDownEvents;
        private List<POINTER_TOUCH_INFO> _pendingUpdateEvents;
        private POINT _firstFingerDownPosition;
        private Timer _delayTimer;

        public PointerInputTargetWindow()
        {
            CreateHandle();
            ResetIdPool();

            _delayTimer = new Timer();
            _delayTimer.Tick += DelayTimer_Tick;
        }

        public int BlockTouchInputThreshold
        {
            get { return _blockTouchInputThreshold; }
            set
            {
                if (IsDisposed || !IsHandleCreated)
                {
                    GestureSign.Common.Log.Logging.LogWarning($"[PointerInputTargetWindow] Cannot set threshold: IsDisposed={IsDisposed}, IsHandleCreated={IsHandleCreated}");
                    return;
                }

                // threshold 修改和注册状态变更必须在 UI 线程上原子执行
                if (InvokeRequired)
                {
                    Invoke(new Action(() => BlockTouchInputThreshold = value));
                    return;
                }

                _blockTouchInputThreshold = value;
                bool flag = _blockTouchInputThreshold >= 2;
                IsRegistered = flag;
            }
        }

        public bool IsRegistered
        {
            get { return _isRegistered; }
            private set
            {
                if (value)
                {
                    if (_isRegistered) return;

                    if (!_isInitialized)
                    {
                        try
                        {
                            NativeMethods.InitializeTouchInjection(10, TOUCH_FEEDBACK.NONE);
                            _isInitialized = true;
                        }
                        catch
                        {
                        }
                    }

                    if (NativeMethods.RegisterPointerInputTarget(Handle, POINTER_INPUT_TYPE.TOUCH))
                    {
                        _isRegistered = true;
                    }
                    else
                    {
                        GestureSign.Common.Log.Logging.LogWarning("[PointerInputTargetWindow] Failed to register as Pointer Input Target - UIAccess may be required");
                    }
                }
                else
                {
                    if (_isRegistered)
                    {
                        if (!NativeMethods.UnregisterPointerInputTarget(Handle, POINTER_INPUT_TYPE.TOUCH))
                        {
                            GestureSign.Common.Log.Logging.LogWarning("[PointerInputTargetWindow] UnregisterPointerInputTarget failed, forcing _isRegistered=false");
                        }
                        _isRegistered = false;
                    }
                }
            }
        }

        public void TemporarilyDisable()
        {
            _tempDisable = true;
        }

        protected sealed override void CreateHandle()
        {
            base.CreateHandle();
            ChangeToMessageOnlyWindow();
        }

        private void ChangeToMessageOnlyWindow()
        {
            IntPtr HWND_MESSAGE = new IntPtr(-3);
            NativeMethods.SetParent(this.Handle, HWND_MESSAGE);
        }

        private void ResetIdPool()
        {
            _idPool.Clear();
            for (int i = 0; i < 10; i++)
            {
                _idPool.Enqueue(i);
            }
        }

        protected override void WndProc(ref Message message)
        {
            switch (message.Msg)
            {
                case NativeMethods.WM_POINTERDOWN:
                case NativeMethods.WM_POINTERUP:
                case NativeMethods.WM_POINTERUPDATE:
                    ProcessPointerMessage(message);
                    return;
            }
            base.WndProc(ref message);
        }

        private void CheckLastError()
        {
            int errCode = Marshal.GetLastWin32Error();
            if (errCode != 0)
            {
                throw new Win32Exception(errCode);
            }
        }

        #region ProcessInput

        private void ProcessPointerMessage(Message message)
        {
            POINTER_TOUCH_INFO[] touchInfos = GetPointerTouchInfos(message);

            if (touchInfos.Length == 0 || touchInfos[0].PointerInfo.FrameID == _lastFrameID) return;
            _lastFrameID = touchInfos[0].PointerInfo.FrameID;

            bool shouldInject = touchInfos.Length < _blockTouchInputThreshold || _tempDisable;

            // 延迟期间收到更多手指 → 取消延迟，BLOCK 所有（包括缓存的 DOWN）
            if (_pendingDownEvents != null && !shouldInject)
            {
                CancelPendingInjection();
            }

            List<POINTER_TOUCH_INFO> ptis = GenerateInput(touchInfos, shouldInject);

            if (shouldInject)
            {
                if (ptis.Count != 0)
                {
                    // 检查是否需要延迟：第一根手指 DOWN 且有延迟配置
                    if (ShouldDelayInjection(ptis))
                    {
                        StartPendingInjection(ptis);
                        return;
                    }

                    // 延迟期间的 UPDATE：检查移动距离
                    if (_pendingDownEvents != null)
                    {
                        bool hasExceededThreshold = false;
                        int moveThreshold = AppConfig.TapDistanceThreshold / 2;

                        foreach (var pti in ptis)
                        {
                            if (pti.PointerInfo.PointerFlags.HasFlag(POINTER_FLAGS.UPDATE))
                            {
                                double dx = pti.PointerInfo.PtPixelLocation.X - _firstFingerDownPosition.X;
                                double dy = pti.PointerInfo.PtPixelLocation.Y - _firstFingerDownPosition.Y;
                                double distance = Math.Sqrt(dx * dx + dy * dy);

                                if (distance >= moveThreshold)
                                {
                                    hasExceededThreshold = true;
                                    break;
                                }
                            }
                        }

                        // 延迟期间手指 UP → flush 缓存并注入 UP（正常 tap）
                        bool hasUp = ptis.Any(p => p.PointerInfo.PointerFlags.HasFlag(POINTER_FLAGS.UP));

                        if (hasExceededThreshold || hasUp)
                        {
                            // 移动超阈值或手指抬起 → 确认为单指操作，注入缓存 + 当前帧
                            FlushPendingInjection();
                            NativeMethods.InjectTouchInput(ptis.Count, ptis.ToArray());
                        }
                        else
                        {
                            // 移动未超阈值，累积 UPDATE
                            _pendingUpdateEvents.AddRange(ptis);
                        }
                        return;
                    }

                    NativeMethods.InjectTouchInput(ptis.Count, ptis.ToArray());
                }
            }
        }

        private POINTER_TOUCH_INFO[] GetPointerTouchInfos(Message message)
        {
            int pointerId = (int)(message.WParam.ToInt64() & 0xffff);
            int pCount = 0;
            if (!NativeMethods.GetPointerFrameTouchInfo(pointerId, ref pCount, null))
            {
                CheckLastError();
            }
            POINTER_TOUCH_INFO[] touchInfos = new POINTER_TOUCH_INFO[pCount];
            if (!NativeMethods.GetPointerFrameTouchInfo(pointerId, ref pCount, touchInfos))
            {
                CheckLastError();
            }
            return touchInfos;
        }

        /// <summary>
        /// 生成待注入的触摸事件，管理 PointerID 映射和 BLOCKED 手指跟踪。
        /// </summary>
        private List<POINTER_TOUCH_INFO> GenerateInput(POINTER_TOUCH_INFO[] touchInfos, bool shouldInject)
        {
            List<POINTER_TOUCH_INFO> ptis = new List<POINTER_TOUCH_INFO>(touchInfos.Length);
            int upFlagCount = 0;

            // BLOCKED 帧中，将所有已映射但未标记为 BLOCKED 的手指标记为 BLOCKED
            if (!shouldInject && _pointerIdList.Count > 0)
            {
                foreach (var kvp in _pointerIdList)
                {
                    if (!_blockedPointerIds.Contains(kvp.Key))
                    {
                        _blockedPointerIds.Add(kvp.Key);
                    }
                }
            }

            foreach (var currentTouchInfo in touchInfos)
            {
                var currentPointerInfo = currentTouchInfo.PointerInfo;
                bool isBlockedFinger = _blockedPointerIds.Contains(currentPointerInfo.PointerID);

                POINTER_INFO pointerInfo = new POINTER_INFO()
                {
                    pointerType = POINTER_INPUT_TYPE.TOUCH,
                    PtPixelLocation = currentPointerInfo.PtPixelLocation,
                };

                if (currentPointerInfo.PointerFlags.HasFlag(POINTER_FLAGS.UPDATE))
                {
                    pointerInfo.PointerFlags = POINTER_FLAGS.INCONTACT | POINTER_FLAGS.INRANGE | POINTER_FLAGS.UPDATE;

                    if (_pointerIdList.TryGetValue(currentPointerInfo.PointerID, out int id))
                    {
                        pointerInfo.PointerID = id;
                    }
                    else continue;

                    if (isBlockedFinger) continue;
                }
                else if (currentPointerInfo.PointerFlags.HasFlag(POINTER_FLAGS.UP))
                {
                    pointerInfo.PointerFlags = POINTER_FLAGS.UP;

                    upFlagCount++;

                    if (_pointerIdList.TryGetValue(currentPointerInfo.PointerID, out int id))
                    {
                        pointerInfo.PointerID = id;
                        _pointerIdList.Remove(currentPointerInfo.PointerID);

                        if (isBlockedFinger)
                        {
                            _blockedPointerIds.Remove(currentPointerInfo.PointerID);
                            _idPool.Enqueue(id);
                            continue;
                        }

                        if (_pointerIdList.Count == 0)
                        {
                            pointerInfo.PointerFlags |= POINTER_FLAGS.INRANGE;
                        }
                        _idPool.Enqueue(id);
                    }
                    else continue;
                }
                else if (currentPointerInfo.PointerFlags.HasFlag(POINTER_FLAGS.DOWN))
                {
                    pointerInfo.PointerFlags = POINTER_FLAGS.DOWN | POINTER_FLAGS.INRANGE | POINTER_FLAGS.INCONTACT;

                    if (_pointerIdList.ContainsKey(currentPointerInfo.PointerID)) continue;

                    if (_idPool.Count > 0)
                    {
                        pointerInfo.PointerID = _idPool.Dequeue();
                        _pointerIdList.Add(currentPointerInfo.PointerID, pointerInfo.PointerID);
                    }

                    if (!shouldInject)
                    {
                        _blockedPointerIds.Add(currentPointerInfo.PointerID);
                        continue;
                    }
                }
                else continue;

                POINTER_TOUCH_INFO pti = new POINTER_TOUCH_INFO()
                {
                    TouchFlags = TOUCH_FLAGS.NONE,
                    PointerInfo = pointerInfo,
                };
                ptis.Add(pti);
            }

            if (upFlagCount == touchInfos.Length)
            {
                _pointerIdList.Clear();
                _blockedPointerIds.Clear();
                ResetIdPool();
                if (_tempDisable)
                {
                    _tempDisable = false;
                }
            }
            return ptis;
        }

        #endregion ProcessInput

        #region DelayedInjection

        private bool ShouldDelayInjection(List<POINTER_TOUCH_INFO> ptis)
        {
            if (_pendingDownEvents != null) return false;
            if (_blockTouchInputThreshold < 2) return false;

            int delay = AppConfig.MultiFingerDelay;
            if (delay <= 0) return false;

            return ptis.Any(p => p.PointerInfo.PointerFlags.HasFlag(POINTER_FLAGS.DOWN));
        }

        private void StartPendingInjection(List<POINTER_TOUCH_INFO> ptis)
        {
            _pendingDownEvents = new List<POINTER_TOUCH_INFO>(ptis);
            _pendingUpdateEvents = new List<POINTER_TOUCH_INFO>();

            var downEvent = ptis.First(p => p.PointerInfo.PointerFlags.HasFlag(POINTER_FLAGS.DOWN));
            _firstFingerDownPosition = downEvent.PointerInfo.PtPixelLocation;

            _delayTimer.Interval = AppConfig.MultiFingerDelay;
            _delayTimer.Start();
        }

        private void FlushPendingInjection()
        {
            if (_pendingDownEvents == null) return;

            _delayTimer.Stop();

            NativeMethods.InjectTouchInput(_pendingDownEvents.Count, _pendingDownEvents.ToArray());

            if (_pendingUpdateEvents.Count > 0)
            {
                NativeMethods.InjectTouchInput(_pendingUpdateEvents.Count, _pendingUpdateEvents.ToArray());
            }

            _pendingDownEvents = null;
            _pendingUpdateEvents = null;
        }

        private void CancelPendingInjection()
        {
            if (_pendingDownEvents == null) return;

            _delayTimer.Stop();

            // 缓存的 DOWN 从未注入，将对应手指标记为 BLOCKED
            foreach (var pti in _pendingDownEvents)
            {
                if (pti.PointerInfo.PointerFlags.HasFlag(POINTER_FLAGS.DOWN))
                {
                    foreach (var kvp in _pointerIdList)
                    {
                        if (kvp.Value == pti.PointerInfo.PointerID)
                        {
                            _blockedPointerIds.Add(kvp.Key);
                            break;
                        }
                    }
                }
            }

            _pendingDownEvents = null;
            _pendingUpdateEvents = null;
        }

        private void DelayTimer_Tick(object sender, EventArgs e)
        {
            _delayTimer.Stop();
            FlushPendingInjection();
        }

        #endregion DelayedInjection

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_NOACTIVATE = 0x08000000;
                const int WS_EX_TOOLWINDOW = 0x00000080;
                CreateParams myParams = base.CreateParams;
                myParams.ExStyle = myParams.ExStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return myParams;
            }
        }
    }
}
