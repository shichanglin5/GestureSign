using GestureSign.Common.Applications;
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

        // 速度计算专用变量（与手势触发分离）
        private Stopwatch _velocityStopwatch = new Stopwatch();
        private List<Point> _velocityLastPoints;
        private readonly Queue<VelocityVector> _velocityHistory = new Queue<VelocityVector>();
        private const int VelocityHistorySize = 3;
        private int _scrollFrameCount;


        // 两指缩放相关变量
        private double _lastFingerDistance;
        private bool _isZooming;
        private readonly PinchZoomInjector _pinchZoomInjector = new PinchZoomInjector();
        private int _lastFingerCount;
        private int _prevFingerCount; // 上一帧的手指数，用于检测手指数变化

        // 缩放进入/退出的连续帧计数
        private int _zoomDetectFrames;       // 连续检测到缩放特征的帧数
        private int _scrollDetectFrames;     // 缩放模式下连续检测到滚动特征的帧数
        private const int ZoomEnterFrames = 3;  // 需要连续多少帧才进入缩放
        private const int ZoomExitFrames = 3;   // 缩放模式下连续多少帧滚动特征才退出

        // InertialScroll 内置执行器（按手指数缓存）
        private readonly Dictionary<int, InertialScrollExecutor> _scrollExecutors = new();

        // 触控屏滚动目标：缓存设备类型和触摸点，传递给 InertialScrollExecutor
        private Devices _lastSourceDevice;
        private Point _lastTouchPoint;

        public ContinuousGestureTrigger()
        {
            _motionThreshold = 20f * DpiHelper.GetSystemDpi() / 96f;

            PointCapture.Instance.PointCaptured += PointCapture_PointCaptured;
            PointCapture.Instance.CaptureEnded += PointCapture_CaptureEnded;
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

            if (_lastFingerCount > 0 &&
                _lastVelocity != null &&
                _scrollFrameCount >= 3 &&
                msSinceLastFrame < 80 &&
                _scrollExecutors.TryGetValue(_lastFingerCount, out var lastExecutor))
            {
                var config = GetEffectiveConfig(_lastFingerCount);
                if (config != null && config.ScrollMode == ContinuousScrollMode.InertialScroll)
                {
                    var window = ApplicationManager.Instance.CaptureWindow;
                    var settings = config.ScrollSettings ?? new InertialScrollSettings();
                    var inertialVelocity = GetAveragedVelocity(_lastVelocity.Value);
                    // Logging.LogDebug($"[CGT] CaptureEnded: frames={_scrollFrameCount} sinceLastFrame={msSinceLastFrame}ms avgVel=({inertialVelocity.VelocityX:F1},{inertialVelocity.VelocityY:F1}) mag={inertialVelocity.Magnitude:F1} minVel={settings.MomentumMinVelocity}");
                    lastExecutor.StartInertiaIfNeeded(inertialVelocity, window, settings, _lastSourceDevice, _lastTouchPoint);
                }
            }
            else if (_scrollFrameCount > 0)
            {
                // 仅在有实际滚动帧时输出，避免短触碰产生大量无用日志
                Logging.LogDebug($"[CGT] CaptureEnded: no inertia - fingers={_lastFingerCount} hasVelocity={_lastVelocity != null} frames={_scrollFrameCount} sinceLastFrame={msSinceLastFrame}ms hasExecutor={_lastFingerCount > 0 && _scrollExecutors.ContainsKey(_lastFingerCount)}");
            }
            _velocityHistory.Clear();
            _scrollFrameCount = 0;
            _lastVelocity = null;
        }

        private void PointCapture_PointCaptured(object sender, PointsCapturedEventArgs e)
        {
            int fingerCount = e.FingerCount > 0 ? e.FingerCount : e.Points.Count;

            var state = PointCapture.Instance.State;
            if ((state != CaptureState.Capturing && state != CaptureState.CapturingInvalid) || fingerCount < 2)
            {
                return;
            }
            bool fingerCountChanged = fingerCount != _prevFingerCount;
            _prevFingerCount = fingerCount;
            _lastFingerCount = fingerCount;
            _lastSourceDevice = PointCapture.Instance.SourceDevice;

            // 从全量轨迹中提取每个触点的最新位置（稳定，数量 = 活跃触点数）
            var latestPoints = GetLatestPoints(e.Points);
            if (latestPoints.Count == 0) return;

            // 触控屏时计算所有手指的中心点作为滚动目标位置
            if (_lastSourceDevice.HasFlag(Devices.TouchScreen))
            {
                int cx = 0, cy = 0;
                foreach (var pt in latestPoints) { cx += pt.X; cy += pt.Y; }
                _lastTouchPoint = new Point(cx / latestPoints.Count, cy / latestPoints.Count);
            }

            // 按 Application 级别查找当前手指数的连续手势配置
            var config = GetEffectiveConfig(fingerCount);
            if (config == null) return;

            bool enableZoom = config.EnableZoom && fingerCount == 2;
            bool enableScroll = config.ScrollMode == ContinuousScrollMode.InertialScroll ||
                                config.ScrollMode == ContinuousScrollMode.Custom;

            if (!enableScroll && !enableZoom) return;

            // 仅首帧输出配置日志，避免每帧重复
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
                // countMismatch 时 delta 为 0，设最小值避免 avgMove=0 让缩放过于容易误触发
                double avgMove = Math.Max(
                    Math.Sqrt((double)deltaX * deltaX + (double)deltaY * deltaY),
                    countMismatch ? _motionThreshold * 0.3 : 0);

                // 注入失败后重置缩放状态，允许重新触发
                if (_isZooming && !_pinchZoomInjector.IsActive)
                {
                    _isZooming = false;
                    _zoomDetectFrames = 0;
                    _scrollDetectFrames = 0;
                }

                // 本帧是否呈现缩放特征：距离变化大于平移量且超过绝对阈值
                bool frameIsZoomLike = Math.Abs(distDelta) > avgMove * 1.2
                    && Math.Abs(distDelta) > _motionThreshold * 0.5;

                if (_isZooming)
                {
                    // 已在缩放模式中：检测是否应退出
                    // 要求距离变化小（非缩放）且有显著平移（明确是滚动）
                    if (Math.Abs(distDelta) < _motionThreshold * 0.3 && avgMove > _motionThreshold * 0.5)
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
                if (!_scrollExecutors.TryGetValue(fingerCount, out var executor))
                {
                    executor = new InertialScrollExecutor();
                    _scrollExecutors[fingerCount] = executor;
                }

                var window = ApplicationManager.Instance.CaptureWindow;
                var settings = config.ScrollSettings ?? new InertialScrollSettings();
                _scrollFrameCount++;
                executor.ProcessFrame(velocity, window, settings, _lastSourceDevice, _lastTouchPoint);

                _lastPoints = latestPoints;
            }
            else if (config.ScrollMode == ContinuousScrollMode.Custom)
            {
                // Custom 模式：根据方向查找命令并执行
                ExecuteCustomMode(config, velocity, deltaX, deltaY, fingerCount, e, latestPoints);
            }
        }

        private void ExecuteCustomMode(ContinuousGestureConfig config, VelocityVector velocity,
            int deltaX, int deltaY, int fingerCount, PointsCapturedEventArgs e, List<Point> latestPoints)
        {
            if (config.DirectionCommands == null || config.DirectionCommands.Count == 0)
                return;

            int deltaXAbs = Math.Abs(deltaX);
            int deltaYAbs = Math.Abs(deltaY);
            bool isHorizontal = deltaXAbs > deltaYAbs;

            if (isHorizontal)
            {
                var rate = GetRateOfFire(deltaXAbs);
                if (rate >= 1)
                {
                    var direction = deltaX > 0 ? Gestures.Right : Gestures.Left;
                    for (int i = 1; i < rate; i++)
                    {
                        ExecuteDirectionCommands(config, direction, e, velocity);
                    }
                    _stopwatch.Restart();
                    _lastPoints = latestPoints;
                }
            }
            else
            {
                var rate = GetRateOfFire(deltaYAbs);
                if (rate >= 1)
                {
                    var direction = deltaY > 0 ? Gestures.Down : Gestures.Up;
                    for (int i = 1; i < rate; i++)
                    {
                        ExecuteDirectionCommands(config, direction, e, velocity);
                    }
                    _stopwatch.Restart();
                    _lastPoints = latestPoints;
                }
            }
        }

        private void ExecuteDirectionCommands(ContinuousGestureConfig config, Gestures direction,
            PointsCapturedEventArgs e, VelocityVector velocity)
        {
            if (config.DirectionCommands == null ||
                !config.DirectionCommands.TryGetValue(direction, out var commands) ||
                commands == null || commands.Count == 0)
                return;

            var window = ApplicationManager.Instance.CaptureWindow;
            var pointInfo = new PointInfo(
                e.FirstCapturedPoints,
                e.Points,
                window,
                System.Threading.SynchronizationContext.Current,
                velocity);

            var mode = PointCapture.Instance.Mode;
            PluginManager.Instance.ExecuteCommands(commands, pointInfo, mode);
        }

        /// <summary>
        /// 按 Application 级别查找指定手指数的有效连续手势配置。
        /// 优先级：UserApp 启用的配置 > GlobalApp 配置（受 InheritBits 控制）。
        /// 注意：RecognizedApplication 匹配到 UserApp 时不包含 GlobalApp，需要单独查找。
        /// </summary>
        private static ContinuousGestureConfig GetEffectiveConfig(int fingerCount)
        {
            var recognizedApps = ApplicationManager.Instance.RecognizedApplication;
            bool userAppHasConfig = false;
            bool userAppInherits = true;

            if (recognizedApps != null)
            {
                foreach (var app in recognizedApps)
                {
                    if (app is IgnoredApp || app is GlobalApp) continue;

                    var settings = app.ContinuousGestures;
                    if (settings == null) continue;

                    // 取第一个匹配的 UserApp 的继承策略，不被后续低优先级应用覆盖
                    userAppInherits = settings.IsInherited(fingerCount);

                    foreach (var cfg in settings.Configs)
                    {
                        if (cfg.ContactCount == fingerCount)
                        {
                            userAppHasConfig = true;
                            if (cfg.IsEnabled)
                                return cfg;
                        }
                    }

                    // 以第一个 UserApp 为准，不继续遍历
                    break;
                }
            }

            // UserApp 有该手指数的配置但未启用，不回退到全局
            if (userAppHasConfig) return null;

            // UserApp 不继承此手指数，不回退
            if (!userAppInherits) return null;

            // 从 GlobalApp 查找
            var globalSettings = ApplicationManager.Instance.GetGlobalApplication()?.ContinuousGestures;
            if (globalSettings != null)
            {
                foreach (var cfg in globalSettings.Configs)
                {
                    if (cfg.ContactCount == fingerCount && cfg.IsEnabled)
                        return cfg;
                }
            }

            return null;
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












