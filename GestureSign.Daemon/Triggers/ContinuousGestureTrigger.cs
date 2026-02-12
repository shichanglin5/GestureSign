using GestureSign.Common.Applications;
using GestureSign.Common.Input;
using GestureSign.Common.Log;
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
        }

        private void PointCapture_PointCaptured(object sender, PointsCapturedEventArgs e)
        {
            int fingerCount = e.FingerCount > 0 ? e.FingerCount : e.Points.Count;

            var state = PointCapture.Instance.State;
            if ((state != CaptureState.Capturing && state != CaptureState.CapturingInvalid) || fingerCount < 2)
            {
                return;
            }

            // 获取当前应用的有效连续手势模式
            var mode = GetEffectiveMode();
            bool enableScroll = mode == ContinuousGestureMode.Scroll || mode == ContinuousGestureMode.ScrollAndZoom;
            bool enableZoom = mode == ContinuousGestureMode.Zoom || mode == ContinuousGestureMode.ScrollAndZoom;

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
            if (enableZoom && fingerCount == 2 && e.FirstCapturedPoints.Count == 2 && _lastFingerDistance > 0)
            {
                double currentDist = PointPatternMath.GetDistance(e.FirstCapturedPoints[0], e.FirstCapturedPoints[1]);
                double distDelta = currentDist - _lastFingerDistance;
                double avgMove = Math.Sqrt((double)deltaX * deltaX + (double)deltaY * deltaY);

                // 滞回：已进入缩放模式后保持，避免每帧重新判定导致丢帧
                bool isPinchZoom = _isZooming
                    || (Math.Abs(distDelta) > avgMove * 1.5 && Math.Abs(distDelta) > _motionThreshold * 0.3);

                if (isPinchZoom)
                {
                    if (!_isZooming)
                    {
                        _pinchZoomInjector.Start(System.Windows.Forms.Cursor.Position);
                        _isZooming = true;
                    }
                    else
                    {
                        double zoomSpeed = GetEffectiveZoomSpeed();
                        _pinchZoomInjector.Update(distDelta, zoomSpeed);
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

            // 查找匹配的连续滑动动作
            var actionsWithContinuousGesture = ApplicationManager.Instance.GetRecognizedDefinedAction(
                a => a != null && a.ContinuousGesture != null &&
                     a.ContinuousGesture.ContactCount == fingerCount);
            if (actionsWithContinuousGesture == null || actionsWithContinuousGesture.Count == 0)
                return;

            int deltaXAbs = Math.Abs(deltaX);
            int deltaYAbs = Math.Abs(deltaY);
            bool isHorizontal = deltaXAbs > deltaYAbs;
            if (isHorizontal)
            {
                var rate = GetRateOfFire(deltaXAbs);
                if (rate >= 1)
                {
                    for (int i = 1; i < rate; i++)
                    {
                        OnGesturerRecognized(fingerCount, deltaX > 0 ? Gestures.Right : Gestures.Left);
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
                    for (int i = 1; i < rate; i++)
                    {
                        OnGesturerRecognized(fingerCount, deltaY > 0 ? Gestures.Down : Gestures.Up);
                    }
                    _stopwatch.Restart();
                    _lastPoints = e.FirstCapturedPoints;
                }
            }
        }

        private static ContinuousGestureMode GetEffectiveMode()
        {
            var recognizedApps = ApplicationManager.Instance.RecognizedApplication;
            ContinuousGestureMode globalMode = ContinuousGestureMode.Inherit;

            if (recognizedApps != null)
            {
                foreach (var app in recognizedApps)
                {
                    if (app is IgnoredApp) continue;

                    if (app is GlobalApp)
                    {
                        globalMode = app.ContinuousGestureMode;
                        continue;
                    }

                    // 非全局应用显式指定了模式
                    if (app.ContinuousGestureMode != ContinuousGestureMode.Inherit)
                        return app.ContinuousGestureMode;
                }
            }

            // 所有非全局应用都是 Inherit，回退到 GlobalApp 设置
            return globalMode == ContinuousGestureMode.Inherit ? ContinuousGestureMode.Scroll : globalMode;
        }

        private double GetEffectiveZoomSpeed()
        {
            var recognizedApps = ApplicationManager.Instance.RecognizedApplication;

            if (recognizedApps != null)
            {
                foreach (var app in recognizedApps)
                {
                    if (app is IgnoredApp) continue;

                    if (app is GlobalApp) continue;

                    if (app.ZoomSpeed > 0)
                        return app.ZoomSpeed;
                }
            }

            var globalApp = ApplicationManager.Instance.GetGlobalApplication();
            if (globalApp.ZoomSpeed > 0)
                return globalApp.ZoomSpeed;

            return 1.0;
        }

        private void OnGesturerRecognized(int contactCount, Gestures gesture)
        {
            var actions = ApplicationManager.Instance.GetRecognizedDefinedAction(a => a.ContinuousGesture != null &&
                a.ContinuousGesture.ContactCount == contactCount &&
                (a.ContinuousGesture.Gesture & gesture) != Gestures.None);
            if (actions.Count > 0)
                OnTriggerFired(new TriggerFiredEventArgs(actions, _startPoint, _lastVelocity));
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
