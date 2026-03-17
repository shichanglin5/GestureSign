﻿﻿using GestureSign.Common.Applications;
using GestureSign.Common.Gestures;
using GestureSign.Common.Input;
using GestureSign.Common.Log;
using GestureSign.Common.Plugins;
using GestureSign.Daemon.Input;
using GestureSign.Daemon.Native;
using GestureSign.PointPatterns;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;

namespace GestureSign.Daemon.Triggers
{
    class ContinuousGestureTrigger : Trigger
    {
        private Point _startPoint;
        private float _motionThreshold;
        private Stopwatch _stopwatch = new Stopwatch();
        private List<Point> _lastPoints;
        private VelocityVector? _lastVelocity;

        // 速度计算专用变量，与手势触发逻辑分离。
        private Stopwatch _velocityStopwatch = new Stopwatch();
        private List<Point> _velocityLastPoints;
        private readonly Queue<VelocityVector> _velocityHistory = new Queue<VelocityVector>();
        private const int VelocityHistorySize = 3;
        private int _scrollFrameCount;
        private ContinuousGestureConfig _activeConfig;
        private GestureModifiers _activeModifiers = GestureModifiers.Default;
        private InertialScrollSettings _activeScrollSettings;
        private bool _activeEnableScroll;
        private bool _activeEnableZoom;
        private PrimaryAxis _lastScrollAxis = PrimaryAxis.None;
        private int _lastScrollSign;


        // 双指缩放相关变量。
        private double _lastFingerDistance;
        private bool _isZooming;
        private readonly PinchZoomInjector _pinchZoomInjector = new PinchZoomInjector();
        private int _lastFingerCount;
        private int _prevFingerCount; // 上一帧的手指数，用于检测手指数变化。

        // 缩放进入/退出的连续帧计数。
        private int _zoomDetectFrames;       // 连续检测到缩放特征的帧数。
        private int _scrollDetectFrames;     // 缩放模式下连续检测到滚动特征的帧数。
        private const int ZoomEnterFrames = 3;  // 需要连续多少帧才能进入缩放。
        private const int ZoomExitFrames = 3;   // 缩放模式下连续多少帧滚动特征才退出。

        // InertialScroll 内置执行器，按手指数缓存。
        private readonly Dictionary<int, InertialScrollExecutor> _scrollExecutors = new();

        // 触控屏滚动目标：缓存设备类型和触摸点，传递给 InertialScrollExecutor。
        private Devices _lastSourceDevice;
        private Point _lastTouchPoint;

        private enum PrimaryAxis
        {
            None,
            X,
            Y,
        }

        public ContinuousGestureTrigger()
        {
            _motionThreshold = 20f * DpiHelper.GetSystemDpi() / 96f;

            PointCapture.Instance.PointCaptured += PointCapture_PointCaptured;
            PointCapture.Instance.CaptureEnded += PointCapture_CaptureEnded;
        }

        internal static bool ShouldHandleTwoFingerContinuous(int fingerCount, int activeFingerCount)
        {
            int resolvedFingerCount = fingerCount > 0 ? fingerCount : activeFingerCount;
            int resolvedActiveFingerCount = activeFingerCount > 0 ? activeFingerCount : resolvedFingerCount;
            return resolvedActiveFingerCount == 2;
        }

        private void PointCapture_CaptureEnded(object sender, System.EventArgs e)
        {
            _stopwatch.Stop();
            _lastPoints = null;
            _velocityStopwatch.Stop();
            _velocityLastPoints = null;
            _lastFingerDistance = 0;
            _prevFingerCount = 0;
            _zoomDetectFrames = 0;
            _scrollDetectFrames = 0;
            if (_isZooming)
            {
                _pinchZoomInjector.Stop();
                _isZooming = false;
            }

            foreach (var executor in _scrollExecutors.Values)
                executor.ResetGestureState();

            // 最后一帧到松手的时间：如果手指静止超过阈值再松开，不触发惯性
            long msSinceLastFrame = _velocityStopwatch.ElapsedMilliseconds;

            int triggerMaxIdleMs = _activeScrollSettings?.MomentumTriggerMaxIdleMs ?? 80;
            if (_lastFingerCount > 0 &&
                _lastVelocity != null &&
                _scrollFrameCount >= 3 &&
                msSinceLastFrame < triggerMaxIdleMs &&
                _scrollExecutors.TryGetValue(_lastFingerCount, out var lastExecutor))
            {
                if (_activeEnableScroll)
                {
                    var window = ApplicationManager.Instance.CaptureWindow;
                    var settings = _activeScrollSettings ?? new InertialScrollSettings();
                    var inertialVelocity = GetAveragedVelocity(_lastVelocity.Value);
                    Logging.LogDebug($"[CGT] CaptureEnded: frames={_scrollFrameCount} sinceLastFrame={msSinceLastFrame}ms avgVel=({inertialVelocity.VelocityX:F1},{inertialVelocity.VelocityY:F1}) mag={inertialVelocity.Magnitude:F1} minVel={settings.MomentumMinVelocity}");
                    lastExecutor.StartInertiaIfNeeded(inertialVelocity, window, settings, _lastSourceDevice, _lastTouchPoint);
                }
            }
            else if (_scrollFrameCount > 0)
            {
                // 仅在有实际滚动帧时输出，避免短触碰产生大量无用日志
                Logging.LogTrace($"[CGT] CaptureEnded: no inertia - fingers={_lastFingerCount} hasVelocity={_lastVelocity != null} frames={_scrollFrameCount} sinceLastFrame={msSinceLastFrame}ms hasExecutor={_lastFingerCount > 0 && _scrollExecutors.ContainsKey(_lastFingerCount)}");
            }
            _velocityHistory.Clear();
            _scrollFrameCount = 0;
            _lastVelocity = null;
            _activeModifiers = GestureModifiers.Default;
            ResetActiveContinuousConfig();
            ResetVelocityDirectionTracking();
        }

        private void PointCapture_PointCaptured(object sender, PointsCapturedEventArgs e)
        {
            int fingerCount = e.FingerCount > 0 ? e.FingerCount : e.Points.Count;
            int activeFingerCount = e.ActiveFingerCount > 0 ? e.ActiveFingerCount : fingerCount;
            int gestureFingerCount = activeFingerCount;

            var state = PointCapture.Instance.State;
            if ((state != CaptureState.Capturing && state != CaptureState.CapturingInvalid) ||
                !ShouldHandleTwoFingerContinuous(fingerCount, activeFingerCount))
            {
                return;
            }

            // 实际活跃手指数不足时停止连续手势
            if (gestureFingerCount < 2)
            {
                return;
            }

            bool fingerCountChanged = gestureFingerCount != _prevFingerCount;
            _prevFingerCount = gestureFingerCount;
            _lastFingerCount = gestureFingerCount;
            _lastSourceDevice = PointCapture.Instance.SourceDevice;

            // 浠庡叏閲忚建杩逛腑鎻愬彇姣忎釜瑙︾偣鐨勬渶鏂颁綅缃紙绋冲畾锛屾暟閲?= 娲昏穬瑙︾偣鏁帮級
            var latestPoints = GetLatestPoints(e.Points);
            if (latestPoints.Count == 0) return;

            // 触控屏时计算所有手指的中心点作为滚动目标位置
            if (_lastSourceDevice.HasFlag(Devices.TouchScreen))
            {
                int cx = 0, cy = 0;
                foreach (var pt in latestPoints) { cx += pt.X; cy += pt.Y; }
                _lastTouchPoint = new Point(cx / latestPoints.Count, cy / latestPoints.Count);
            }

            if (_lastPoints == null || fingerCountChanged)
            {
                if (_lastPoints == null)
                {
                    _activeModifiers = PointCapture.Instance.GetCurrentModifiers();
                    // 首帧强制刷新前台窗口识别，避免窗口切换后 _recognizedApplication 仍为旧值
                    // （触控板两指先后落下时，CaptureStarted 以 FingerCount=1 触发会跳过刷新）
                    ApplicationManager.Instance.GetForegroundApplications();
                }
                InitializeActiveContinuousConfig(gestureFingerCount);
            }

            var config = _activeConfig;
            // 修饰符过滤：配置的修饰符必须与采样时的修饰符一致
            if (config != null && config.Modifiers != _activeModifiers)
                config = null;
            if (config == null) return;

            bool enableZoom = _activeEnableZoom;
            bool enableScroll = _activeEnableScroll;

            if (!enableScroll && !enableZoom) return;

            // 仅惯性输出配置日志，避免每帧重复
            // if (_lastPoints == null)
            //     Logging.LogDebug($"[CGT] config: fingers={fingerCount} zoom={enableZoom} scrollMode={config.ScrollMode}");

            // 缩放未启用时重置计数器，防止 enableZoom 动态切换时残留状态
            if (!enableZoom && (_zoomDetectFrames > 0 || _scrollDetectFrames > 0))
            {
                _zoomDetectFrames = 0;
                _scrollDetectFrames = 0;
            }

            // 初始化跟踪变量（首帧或手指数变化时）
            if (_lastPoints == null || fingerCountChanged)
            {
                foreach (var executor in _scrollExecutors.Values)
                    executor.StopInertia();

                _startPoint = latestPoints[0];
                _lastPoints = latestPoints;
                _stopwatch.Restart();
                _lastVelocity = null;
                _velocityHistory.Clear();
                _scrollFrameCount = 0;
                _zoomDetectFrames = 0;
                _scrollDetectFrames = 0;
                _velocityLastPoints = latestPoints;
                _velocityStopwatch.Restart();
                ResetVelocityDirectionTracking();
                if (e.FirstCapturedPoints.Count >= 2)
                    _lastFingerDistance = PointPatternMath.GetDistance(e.FirstCapturedPoints[0], e.FirstCapturedPoints[1]);
                return;
            }

            // 计算当前速度
            var velocity = CalculateVelocity(latestPoints, _velocityLastPoints, _velocityStopwatch.ElapsedMilliseconds);
            _lastVelocity = velocity;
            // 仅有效速度帧进入历史，避免零速度帧压低惯性起始速度
            // 暂停后松开的问题已通过 msSinceLastFrame 阈值判定解决
            if (velocity.Magnitude > 0)
            {
                TrackVelocityDirectionChange(velocity);
                _velocityHistory.Enqueue(velocity);
                while (_velocityHistory.Count > VelocityHistorySize)
                    _velocityHistory.Dequeue();
            }
            _velocityLastPoints = latestPoints;
            _velocityStopwatch.Restart();

            // 计算平均位移（触点数不一致时 delta 设为 0，跳过滚动但不影响缩放检测）
            int deltaX = 0, deltaY = 0;
            bool countMismatch = _lastPoints.Count != latestPoints.Count;
            if (!countMismatch)
            {
                int pointCount = latestPoints.Count;
                for (int i = 0; i < pointCount; i++)
                {
                    deltaX += latestPoints[i].X - _lastPoints[i].X;
                    deltaY += latestPoints[i].Y - _lastPoints[i].Y;
                }
                deltaX /= pointCount;
                deltaY /= pointCount;
            }

            // 两指缩放检测（使用 FirstCapturedPoints 获取两个手指的位置，
            // 因为 latestPoints 来自 _pointsCaptured 只含 feature finger）
            if (enableZoom && e.FirstCapturedPoints.Count >= 2)
            {
                double currentDist = PointPatternMath.GetDistance(e.FirstCapturedPoints[0], e.FirstCapturedPoints[1]);

                // 首次获得两指数据时初始化基线距离，跳过缩放检测但继续执行滚动逻辑
                if (_lastFingerDistance <= 0)
                {
                    _lastFingerDistance = currentDist;
                }
                else
                {
                double distDelta = currentDist - _lastFingerDistance;
                double zoomSensitivity = Math.Clamp(config.ZoomSensitivity > 0 ? config.ZoomSensitivity : 1.0, 0.1, 3.0);
                // 使用帧间位移（velocity.Delta*）而非 deltaX/deltaY 计算平移量
                // deltaX/deltaY 在 Custom 模式下是累积值（用于 GetRateOfFire），不适合缩放检测
                // countMismatch 时 delta 为 0，设最小值避免 avgMove=0 让缩放过于容易误触发
                double avgMove = Math.Max(
                    Math.Sqrt(velocity.DeltaX * velocity.DeltaX + velocity.DeltaY * velocity.DeltaY),
                    countMismatch ? _motionThreshold * 0.3 : 0);

                // 注入失败后重置缩放状态，允许重新触发
                if (_isZooming && !_pinchZoomInjector.IsActive)
                {
                    _isZooming = false;
                    _zoomDetectFrames = 0;
                    _scrollDetectFrames = 0;
                }

                // 本帧是否呈现缩放特征：距离变化大于平移量且超过绝对阈值
                // zoomSensitivity 控制绝对阈值：值越小越容易进入缩放
                bool frameIsZoomLike = Math.Abs(distDelta) > avgMove * 1.2
                    && Math.Abs(distDelta) > _motionThreshold * 0.5 * zoomSensitivity;

                if (_isZooming)
                {
                    // 已在缩放模式中：检测是否应退出
                    // 要求距离变化小（非缩放）且有显著平移（明确是滚动）
                    if (Math.Abs(distDelta) < _motionThreshold * 0.3 * zoomSensitivity && avgMove > _motionThreshold * 0.5)
                    {
                        // 本帧明显是滚动而非缩放
                        _scrollDetectFrames++;
                    }
                    else
                    {
                        _scrollDetectFrames = 0;
                    }

                    if (_scrollDetectFrames >= ZoomExitFrames)
                    {
                        // 连续多帧检测到滚动特征，退出缩放模式
                        Logging.LogDebug($"[CGT] zoom exit: consecutive scroll frames={_scrollDetectFrames}");
                        _pinchZoomInjector.Stop();
                        _isZooming = false;
                        _zoomDetectFrames = 0;
                        _scrollDetectFrames = 0;
                    }
                    else
                    {
                        // 仍在缩放模式中，更新缩放
                        _pinchZoomInjector.Update(distDelta, config.ZoomSpeed > 0 ? config.ZoomSpeed : 1.0, deltaX, deltaY);

                        // zoom 期间清除滚动速度历史，防止松手时误触发惯性滚动
                        _lastVelocity = null;
                        _velocityHistory.Clear();
                        _scrollFrameCount = 0;

                        _lastFingerDistance = currentDist;
                        _lastPoints = latestPoints;
                        _stopwatch.Restart();
                        return;
                    }
                }
                else
                {
                    // 未在缩放模式：检测是否应进入
                    if (frameIsZoomLike)
                    {
                        _zoomDetectFrames++;
                    }
                    else
                    {
                        // 递减而非清零，容忍触控板上偶尔的非缩放帧（手指数波动等）
                        if (_zoomDetectFrames > 0) _zoomDetectFrames--;
                    }

                    if (_zoomDetectFrames >= ZoomEnterFrames)
                    {
                        // 连续多帧检测到缩放特征，进入缩放模式
                        Logging.LogDebug($"[CGT] zoom enter: consecutive zoom frames={_zoomDetectFrames} distDelta={distDelta:F1} avgMove={avgMove:F1}");
                        _pinchZoomInjector.Start(System.Windows.Forms.Cursor.Position);
                        _isZooming = _pinchZoomInjector.IsActive;
                        if (_isZooming)
                        {
                            _zoomDetectFrames = 0;
                            _scrollDetectFrames = 0;

                            // zoom 期间清除滚动速度历史
                            _lastVelocity = null;
                            _velocityHistory.Clear();
                            _scrollFrameCount = 0;

                            _lastFingerDistance = currentDist;
                            _lastPoints = latestPoints;
                            _stopwatch.Restart();
                            return;
                        }
                    }
                }

                _lastFingerDistance = currentDist;
                } // else (_lastFingerDistance > 0)
            }

            // 缩放模式中不执行滚动逻辑（即使当帧 FirstCapturedPoints.Count < 2）
            if (_isZooming) return;

            // 触点数不一致时跳过滚动逻辑（delta 不可靠），仅更新 _lastPoints
            if (countMismatch)
            {
                _lastPoints = latestPoints;
                return;
            }

            // 连续滑动逻辑
            if (!enableScroll) return;

            if (config.ScrollMode == ContinuousScrollMode.InertialScroll)
            {
                // InertialScroll 模式：内置执行，不经过 Action/Plugin 链路
                if (!_scrollExecutors.TryGetValue(gestureFingerCount, out var executor))
                {
                    executor = new InertialScrollExecutor();
                    _scrollExecutors[gestureFingerCount] = executor;
                }

                var window = ApplicationManager.Instance.CaptureWindow;
                var settings = _activeScrollSettings ?? new InertialScrollSettings();
                _scrollFrameCount++;
                // Logging.LogTrace($"[CGT] scroll frame={_scrollFrameCount} dt={_velocityStopwatch.ElapsedMilliseconds}ms dx={velocity.DeltaX:F1} dy={velocity.DeltaY:F1} vx={velocity.VelocityX:F0} vy={velocity.VelocityY:F0}");
                if (velocity.DeltaX == 0 && velocity.DeltaY == 0)
                {
                    _lastPoints = latestPoints;
                    return;
                }
                executor.ProcessFrame(velocity, window, settings, _lastSourceDevice, _lastTouchPoint);

                _lastPoints = latestPoints;
            }

        }
        /// <summary>
        /// 按 Application 级别查找指定手指数的有效连续手势配置。
        /// 优先级：UserApp 启用的配置 > GlobalApp 配置（受 InheritBits 控制）。
        /// 注意：RecognizedApplication 匹配到 UserApp 时不包含 GlobalApp，需要单独查找。
        /// </summary>
        private static ContinuousGestureConfig GetEffectiveConfig(int fingerCount)
        {
            if (fingerCount != 2)
                return null;

            var recognizedApps = ApplicationManager.Instance.RecognizedApplication;

            if (recognizedApps != null)
            {
                foreach (var app in recognizedApps)
                {
                    if (app is IgnoredApp || app is GlobalApp) continue;

                    var cfg = CreateConfigFromTwoFingerSettings(app.TwoFingerGestures, ApplicationManager.Instance.GetGlobalApplication()?.TwoFingerGestures);
                    if (cfg != null)
                        return cfg;

                    break;
                }
            }

            return CreateConfigFromTwoFingerSettings(ApplicationManager.Instance.GetGlobalApplication()?.TwoFingerGestures, null);
        }

        private static ContinuousGestureConfig CreateConfigFromTwoFingerSettings(TwoFingerGestureSettings settings, TwoFingerGestureSettings inherited)
        {
            if (settings == null && inherited == null)
                return null;

            bool enableScroll = ResolveInheritSwitch(settings?.Scroll ?? InheritSwitch.Inherit, inherited?.Scroll ?? InheritSwitch.Disabled);
            bool enableZoom = ResolveInheritSwitch(settings?.Zoom ?? InheritSwitch.Inherit, inherited?.Zoom ?? InheritSwitch.Disabled);

            bool useLocalScrollSettings = settings != null && settings.Scroll != InheritSwitch.Inherit;
            bool useLocalZoomSettings = settings != null && settings.Zoom != InheritSwitch.Inherit;

            if (!enableScroll && !enableZoom)
                return null;

            return new ContinuousGestureConfig
            {
                IsEnabled = true,
                ContactCount = 2,
                EnableZoom = enableZoom,
                ZoomSpeed = useLocalZoomSettings ? (settings?.ZoomSettings?.ZoomSpeed ?? 1.0) : (inherited?.ZoomSettings?.ZoomSpeed ?? 1.0),
                ZoomSensitivity = useLocalZoomSettings ? (settings?.ZoomSettings?.ZoomSensitivity ?? 1.0) : (inherited?.ZoomSettings?.ZoomSensitivity ?? 1.0),
                ScrollMode = enableScroll ? ContinuousScrollMode.InertialScroll : ContinuousScrollMode.None,
                ScrollSettings = useLocalScrollSettings ? (settings?.ScrollSettings ?? new InertialScrollSettings()) : (inherited?.ScrollSettings ?? new InertialScrollSettings()),
                DirectionCommands = null,
            };
        }

        private static bool ResolveInheritSwitch(InheritSwitch value, InheritSwitch inherited)
        {
            return value switch
            {
                InheritSwitch.Enabled => true,
                InheritSwitch.Disabled => false,
                _ => inherited == InheritSwitch.Enabled,
            };
        }

        private double GetRateOfFire(int distance)
        {
            var deltaTime = _stopwatch.ElapsedMilliseconds;
            if (deltaTime < 2)
                return 0;

            var velocity = distance / (double)deltaTime;
            if (velocity < 3)
            {
                return distance / _motionThreshold;
            }
            else
            {
                if (velocity > 16)
                    velocity = 16;
                return distance / ((-0.0023 * velocity * velocity + 0.0096 * velocity + 0.89) * _motionThreshold);
            }
        }

        private VelocityVector CalculateVelocity(List<Point> currentPoints, List<Point> previousPoints, long deltaTimeMs)
        {
            if (previousPoints == null || deltaTimeMs < 2)
                return new VelocityVector();

            int count = currentPoints.Count;
            if (count == 0 || count != previousPoints.Count)
                return new VelocityVector();

            double deltaX = 0, deltaY = 0;
            for (int i = 0; i < count; i++)
            {
                deltaX += currentPoints[i].X - previousPoints[i].X;
                deltaY += currentPoints[i].Y - previousPoints[i].Y;
            }
            deltaX /= count;
            deltaY /= count;

            double velocityX = (deltaX / deltaTimeMs) * 1000;
            double velocityY = (deltaY / deltaTimeMs) * 1000;

            return new VelocityVector(velocityX, velocityY, deltaX, deltaY);
        }

        private VelocityVector GetAveragedVelocity(VelocityVector fallback)
        {
            if (_velocityHistory.Count == 0)
                return fallback;

            double sumVx = 0;
            double sumVy = 0;
            double sumDx = 0;
            double sumDy = 0;
            int count = 0;

            foreach (var v in _velocityHistory)
            {
                sumVx += v.VelocityX;
                sumVy += v.VelocityY;
                sumDx += v.DeltaX;
                sumDy += v.DeltaY;
                count++;
            }

            if (count == 0)
                return fallback;

            return new VelocityVector(sumVx / count, sumVy / count, sumDx / count, sumDy / count);
        }

        private void InitializeActiveContinuousConfig(int fingerCount)
        {
            _activeConfig = GetEffectiveConfig(fingerCount);
            _activeScrollSettings = _activeConfig?.ScrollSettings ?? new InertialScrollSettings();
            _activeEnableZoom = _activeConfig?.EnableZoom == true;
            _activeEnableScroll = _activeConfig?.ScrollMode == ContinuousScrollMode.InertialScroll;
        }

        private void ResetActiveContinuousConfig()
        {
            _activeConfig = null;
            _activeScrollSettings = null;
            _activeEnableZoom = false;
            _activeEnableScroll = false;
        }

        private void ResetVelocityDirectionTracking()
        {
            _lastScrollAxis = PrimaryAxis.None;
            _lastScrollSign = 0;
        }

        private void TrackVelocityDirectionChange(VelocityVector velocity)
        {
            if (!_activeEnableScroll || _activeScrollSettings == null)
                return;

            var (axis, sign) = GetVelocityPrimaryDirection(velocity, _activeScrollSettings);
            if (axis == PrimaryAxis.None || sign == 0)
                return;

            if (_lastScrollAxis == axis && _lastScrollSign != 0 && sign != _lastScrollSign)
            {
                _velocityHistory.Clear();
            }

            _lastScrollAxis = axis;
            _lastScrollSign = sign;
        }

        private static (PrimaryAxis axis, int sign) GetVelocityPrimaryDirection(VelocityVector velocity, InertialScrollSettings settings)
        {
            double deltaX = velocity.DeltaX * (settings.ReverseHorizontalDirection ? 1 : -1);
            double deltaY = velocity.DeltaY * (settings.ReverseDirection ? -1 : 1);

            if (settings.Direction == ScrollDirection.Vertical)
                deltaX = 0;
            else if (settings.Direction == ScrollDirection.Horizontal)
                deltaY = 0;

            if (Math.Abs(deltaX) < 0.001 && Math.Abs(deltaY) < 0.001)
                return (PrimaryAxis.None, 0);

            if (Math.Abs(deltaY) >= Math.Abs(deltaX))
                return (PrimaryAxis.Y, Math.Sign(deltaY));

            return (PrimaryAxis.X, Math.Sign(deltaX));
        }

        /// <summary>
        /// 从全量轨迹中提取每个触点的最新位置。
        /// e.Points 是 _pointsCaptured.Values 的快照，每个 List&lt;Point&gt; 是一个触点的完整轨迹。
        /// 返回的列表数量稳定（= 活跃触点数），不受单帧报告触点数波动的影响。
        ///
        /// 顺序稳定性依赖：_pointsCaptured 是 Dictionary，其键在 TryBeginCapture 时一次性插入且手势期间仅增不删，
        /// 因此 Values 的迭代顺序等于插入顺序，跨帧稳定。若后续维护中引入键的删除或重建，需重新评估此假设。
        /// </summary>
        private static List<Point> GetLatestPoints(List<List<Point>> pointTraces)
        {
            var result = new List<Point>(pointTraces.Count);
            foreach (var trace in pointTraces)
            {
                if (trace.Count > 0)
                    result.Add(trace[trace.Count - 1]);
            }
            return result;
        }

    }
}








