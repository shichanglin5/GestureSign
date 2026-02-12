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

        // 两指缩放相关变量
        private double _lastFingerDistance;
        private bool _isZooming;
        private readonly PinchZoomInjector _pinchZoomInjector = new PinchZoomInjector();

        // InertialScroll 内置执行器（按手指数缓存）
        private readonly Dictionary<int, InertialScrollExecutor> _scrollExecutors = new();

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
            if (_isZooming)
            {
                _pinchZoomInjector.Stop();
                _isZooming = false;
            }

            foreach (var executor in _scrollExecutors.Values)
                executor.Reset();
        }

        private void PointCapture_PointCaptured(object sender, PointsCapturedEventArgs e)
        {
            int fingerCount = e.FingerCount > 0 ? e.FingerCount : e.Points.Count;

            var state = PointCapture.Instance.State;
            if ((state != CaptureState.Capturing && state != CaptureState.CapturingInvalid) || fingerCount < 2)
            {
                return;
            }

            // 从 Application 级别查找当前手指数的连续手势配置
            var config = GetEffectiveConfig(fingerCount);
            if (config == null) return;

            bool enableZoom = config.EnableZoom && fingerCount == 2;
            bool enableScroll = config.ScrollMode == ContinuousScrollMode.InertialScroll ||
                                config.ScrollMode == ContinuousScrollMode.Custom;

            Logging.LogDebug($"[CGT] config: fingers={fingerCount} zoom={enableZoom} scrollMode={config.ScrollMode}");

            if (!enableScroll && !enableZoom) return;

            // 初始化跟踪变量（首帧或手指数变化时）
            if (_lastPoints == null || _lastPoints.Count != e.FirstCapturedPoints.Count)
            {
                _startPoint = e.FirstCapturedPoints[0];
                _lastPoints = e.FirstCapturedPoints;
                _stopwatch.Restart();
                _lastVelocity = null;
                _velocityLastPoints = e.FirstCapturedPoints;
                _velocityStopwatch.Restart();
                if (e.FirstCapturedPoints.Count == 2)
                    _lastFingerDistance = PointPatternMath.GetDistance(e.FirstCapturedPoints[0], e.FirstCapturedPoints[1]);
                return;
            }

            // 计算当前速度
            var velocity = CalculateVelocity(e.FirstCapturedPoints, _velocityLastPoints, _velocityStopwatch.ElapsedMilliseconds);
            _lastVelocity = velocity;
            _velocityLastPoints = e.FirstCapturedPoints;
            _velocityStopwatch.Restart();

            int deltaX = 0, deltaY = 0;
            for (int i = 0; i < _lastPoints.Count; i++)
            {
                deltaX += e.FirstCapturedPoints[i].X - _lastPoints[i].X;
                deltaY += e.FirstCapturedPoints[i].Y - _lastPoints[i].Y;
            }
            deltaX /= _lastPoints.Count;
            deltaY /= _lastPoints.Count;

            // 两指缩放检测
            if (enableZoom && e.FirstCapturedPoints.Count == 2 && _lastFingerDistance > 0)
            {
                double currentDist = PointPatternMath.GetDistance(e.FirstCapturedPoints[0], e.FirstCapturedPoints[1]);
                double distDelta = currentDist - _lastFingerDistance;
                double avgMove = Math.Sqrt((double)deltaX * deltaX + (double)deltaY * deltaY);

                Logging.LogDebug($"[CGT] zoom: dist={currentDist:F1} delta={distDelta:F1} avg={avgMove:F1} threshold={_motionThreshold * 0.3:F1} isZooming={_isZooming}");

                // 注入失败后重置缩放状态，允许重新触发
                if (_isZooming && !_pinchZoomInjector.IsActive)
                    _isZooming = false;

                // 滞回：已进入缩放模式后保持，避免每帧重新判定导致丢帧
                bool isPinchZoom = _isZooming
                    || (Math.Abs(distDelta) > avgMove * 1.5 && Math.Abs(distDelta) > _motionThreshold * 0.3);

                if (isPinchZoom)
                {
                    if (!_isZooming)
                    {
                        _pinchZoomInjector.Start(System.Windows.Forms.Cursor.Position);
                        _isZooming = _pinchZoomInjector.IsActive;
                    }
                    else
                    {
                        _pinchZoomInjector.Update(distDelta, config.ZoomSpeed > 0 ? config.ZoomSpeed : 1.0, deltaX, deltaY);
                    }

                    _lastFingerDistance = currentDist;
                    _lastPoints = e.FirstCapturedPoints;
                    _stopwatch.Restart();
                    return;
                }

                _lastFingerDistance = currentDist;
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
                executor.ProcessFrame(velocity, window, settings);

                _lastPoints = e.FirstCapturedPoints;
            }
            else if (config.ScrollMode == ContinuousScrollMode.Custom)
            {
                // Custom 模式：根据方向查找命令并执行
                ExecuteCustomMode(config, velocity, deltaX, deltaY, fingerCount, e);
            }
        }

        private void ExecuteCustomMode(ContinuousGestureConfig config, VelocityVector velocity,
            int deltaX, int deltaY, int fingerCount, PointsCapturedEventArgs e)
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
                    _lastPoints = e.FirstCapturedPoints;
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
                    _lastPoints = e.FirstCapturedPoints;
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
        /// 从 Application 级别查找指定手指数的有效连续手势配置
        /// 优先级：UserApp 启用的配置 > GlobalApp 配置（受 InheritBits 控制）
        /// 注意：RecognizedApplication 匹配到 UserApp 时不包含 GlobalApp，需要单独查找
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
            if (previousPoints == null || currentPoints.Count != previousPoints.Count || deltaTimeMs < 2)
                return new VelocityVector();

            double deltaX = 0, deltaY = 0;
            for (int i = 0; i < currentPoints.Count; i++)
            {
                deltaX += currentPoints[i].X - previousPoints[i].X;
                deltaY += currentPoints[i].Y - previousPoints[i].Y;
            }
            deltaX /= currentPoints.Count;
            deltaY /= currentPoints.Count;

            double velocityX = (deltaX / deltaTimeMs) * 1000;
            double velocityY = (deltaY / deltaTimeMs) * 1000;

            return new VelocityVector(velocityX, velocityY, deltaX, deltaY);
        }
    }
}
