using GestureSign.Common.Configuration;
using GestureSign.Daemon.Native;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
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

        public PointerInputTargetWindow()
        {
            CreateHandle();
            ResetIdPool();
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
                // 避免 _blockTouchInputThreshold 被非 UI 线程修改后、注册状态尚未同步时
                // WndProc 中的 ProcessPointerMessage 读到不一致的 threshold 值
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
                            // InitializeTouchInjection requires UIAccess, but RegisterPointerInputTarget might still work
                            // so we continue anyway
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
                //case NativeMethods.WM_POINTERENTER:
                //case NativeMethods.WM_POINTERLEAVE:
                //case NativeMethods.WM_POINTERCAPTURECHANGED:
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

            GestureSign.Common.Log.Logging.LogTrace($"[PointerInputTargetWindow] ProcessPointerMessage: fingers={touchInfos.Length}, threshold={_blockTouchInputThreshold}, tempDisable={_tempDisable}, shouldInject={shouldInject}");

            // 传入 shouldInject 决定是否为 BLOCKED 帧，GenerateInput 内部会跟踪被 BLOCKED 的手指
            List<POINTER_TOUCH_INFO> ptis = GenerateInput(touchInfos, shouldInject);

            if (shouldInject)
            {
                if (ptis.Count != 0)
                {
                    NativeMethods.InjectTouchInput(ptis.Count, ptis.ToArray());
                }
            }
            else
            {
                GestureSign.Common.Log.Logging.LogDebug($"[PointerInputTargetWindow] BLOCKED: {touchInfos.Length} fingers (threshold={_blockTouchInputThreshold})");
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
        /// 当 shouldInject=false 时（BLOCKED 帧），记录 DOWN 事件的手指到 _blockedPointerIds。
        /// 当 shouldInject=true 时（注入帧），跳过之前被 BLOCKED 的手指（其 DOWN 未注入）。
        /// </summary>
        private List<POINTER_TOUCH_INFO> GenerateInput(POINTER_TOUCH_INFO[] touchInfos, bool shouldInject)
        {
            List<POINTER_TOUCH_INFO> ptis = new List<POINTER_TOUCH_INFO>(touchInfos.Length);
            int upFlagCount = 0;

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

                    // 被 BLOCKED 的手指的 UPDATE 事件：保持 ID 映射但不生成注入事件
                    if (isBlockedFinger) continue;
                }
                else if (currentPointerInfo.PointerFlags.HasFlag(POINTER_FLAGS.UP))
                {
                    pointerInfo.PointerFlags = POINTER_FLAGS.UP;

                    upFlagCount++;

                    if (_pointerIdList.TryGetValue(currentPointerInfo.PointerID, out int id))
                    {
                        pointerInfo.PointerID = id;
                        _idPool.Enqueue(id);
                        _pointerIdList.Remove(currentPointerInfo.PointerID);
                        if (_pointerIdList.Count == 0)
                        {
                            pointerInfo.PointerFlags |= POINTER_FLAGS.INRANGE;
                        }
                    }
                    else continue;

                    // 被 BLOCKED 的手指的 UP 事件：清理跟踪状态但不生成注入事件
                    if (isBlockedFinger)
                    {
                        _blockedPointerIds.Remove(currentPointerInfo.PointerID);
                        continue;
                    }
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

                    // BLOCKED 帧中的 DOWN 事件：记录该手指为 BLOCKED，不生成注入事件
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

