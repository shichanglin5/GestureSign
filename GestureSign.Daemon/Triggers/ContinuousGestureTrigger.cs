using GestureSign.Common.Applications;
using GestureSign.Common.Gestures;
using GestureSign.Common.Input;
using GestureSign.Common.Log;
using GestureSign.Daemon.Input;
using GestureSign.Daemon.Native;
using GestureSign.PointPatterns;
using ManagedWinapi.Hooks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;

namespace GestureSign.Daemon.Triggers
{
    class ContinuousGestureTrigger : Trigger
    {
        private Point _startPoint;
        private float _motionThreshold;
        private Stopwatch _stopwatch = new Stopwatch();
        private List<Point> _lastPoints;
        private VelocityVector? _lastVelocity;  // 新增:存储最后计算的速度

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
        }

        private void PointCapture_PointCaptured(object sender, PointsCapturedEventArgs e)
        {
            // 使用 FingerCount 而不是 Points.Count，因为可能只有部分手指有移动轨迹
            int fingerCount = e.FingerCount > 0 ? e.FingerCount : e.Points.Count;

            if (PointCapture.Instance.State != CaptureState.Capturing || fingerCount < 2)
            {
                Logging.LogDebug($"[ContinuousGestureTrigger] Skip: State={PointCapture.Instance.State}, FingerCount={fingerCount}, Points={e.Points.Count}");
                return;
            }

            // 先获取匹配的连续手势动作（ApplicationManager 会优先返回当前应用的，如果没有再返回全局的）
            var actionsWithContinuousGesture = ApplicationManager.Instance.GetRecognizedDefinedAction(a => a != null && a.ContinuousGesture != null);
            if (actionsWithContinuousGesture == null || actionsWithContinuousGesture.Count == 0)
            {
                Logging.LogDebug($"[ContinuousGestureTrigger] No continuous gesture actions configured");
                return;
            }

            // 检查找到的连续手势是否来自当前应用（非全局）
            var recognizedApps = ApplicationManager.Instance.RecognizedApplication;
            bool hasCurrentAppContinuousGesture = false;
            if (recognizedApps != null && recognizedApps.Any())
            {
                hasCurrentAppContinuousGesture = recognizedApps
                    .Where(app => !(app is GlobalApp) && !(app is IgnoredApp) && app.Actions != null)
                    .SelectMany(app => app.Actions)
                    .Any(a => a != null && a.ContinuousGesture != null);
            }

            // 新逻辑：只有当全局配置的是N指连续手势时，且当前应用也配置了N指手势（连续或绘制）
            // 才完全忽略全局连续手势，以应用的为准
            if (!hasCurrentAppContinuousGesture && recognizedApps != null && recognizedApps.Any())
            {
                // 获取当前应用的所有动作
                var currentAppActions = recognizedApps
                    .Where(app => !(app is GlobalApp) && !(app is IgnoredApp) && app.Actions != null)
                    .SelectMany(app => app.Actions)
                    .ToList();

                bool hasCurrentAppGesture = false;

                // 检查当前应用是否有同手指数的连续手势
                if (currentAppActions.Any(a => a != null && a.ContinuousGesture != null &&
                                               a.ContinuousGesture.ContactCount == fingerCount))
                {
                    hasCurrentAppGesture = true;
                    Logging.LogDebug($"[ContinuousGestureTrigger] Current app has {fingerCount}-finger continuous gesture");
                }
                // 检查当前应用是否有同手指数的绘制手势
                else
                {
                    foreach (var action in currentAppActions.Where(a => a != null && !string.IsNullOrEmpty(a.GestureName)))
                    {
                        var gesture = GestureManager.Instance.GetNewestGestureSample(action.GestureName);
                        if (gesture != null && gesture.FingerCount == fingerCount)
                        {
                            hasCurrentAppGesture = true;
                            Logging.LogDebug($"[ContinuousGestureTrigger] Current app has {fingerCount}-finger drawn gesture '{action.GestureName}'");
                            break;
                        }
                    }
                }

                // 如果当前应用有同手指数的任意手势，则忽略全局的连续手势
                if (hasCurrentAppGesture)
                {
                    Logging.LogDebug($"[ContinuousGestureTrigger] Skip: Current app has {fingerCount}-finger gesture, ignore global continuous gesture");
                    return;
                }
            }

            // 继续执行连续手势逻辑
            if (_lastPoints == null || _lastPoints.Count != e.FirstCapturedPoints.Count)
            {
                _startPoint = e.FirstCapturedPoints[0];
                _lastPoints = e.FirstCapturedPoints;
                _stopwatch.Restart();
                _lastVelocity = null;  // 重置速度
                return;
            }

            // 计算当前速度
            var velocity = CalculateVelocity(e.FirstCapturedPoints, _lastPoints, _stopwatch.ElapsedMilliseconds);
            _lastVelocity = velocity;

            int deltaX = 0, deltaY = 0;
            for (int i = 0; i < _lastPoints.Count; i++)
            {
                deltaX += e.FirstCapturedPoints[i].X - _lastPoints[i].X;
                deltaY += e.FirstCapturedPoints[i].Y - _lastPoints[i].Y;
            }
            deltaX /= _lastPoints.Count;
            deltaY /= _lastPoints.Count;
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

        private void OnGesturerRecognized(int contactCount, Gestures gesture)
        {
            // Use bitwise AND to match gesture flags (supports Gestures.All, Gestures.Vertical, etc.)
            var actions = ApplicationManager.Instance.GetRecognizedDefinedAction(a => a.ContinuousGesture != null &&
                a.ContinuousGesture.ContactCount == contactCount &&
                (a.ContinuousGesture.Gesture & gesture) != Gestures.None);

            Logging.LogDebug($"[ContinuousGestureTrigger] Gesture recognized: {gesture}, ContactCount={contactCount}, Actions={actions.Count}, Velocity={_lastVelocity?.Magnitude:F1} px/s");

            if (actions.Count > 0)
                OnTriggerFired(new TriggerFiredEventArgs(actions, _startPoint, _lastVelocity));  // 传递速度信息
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

        /// <summary>
        /// 计算手势滑动速度
        /// </summary>
        /// <param name="currentPoints">当前触点位置</param>
        /// <param name="previousPoints">上次触点位置</param>
        /// <param name="deltaTimeMs">时间间隔(毫秒)</param>
        /// <returns>速度向量</returns>
        private VelocityVector CalculateVelocity(List<Point> currentPoints, List<Point> previousPoints, long deltaTimeMs)
        {
            if (previousPoints == null || currentPoints.Count != previousPoints.Count || deltaTimeMs < 2)
                return new VelocityVector();

            // 计算所有手指的平均位移
            int deltaX = 0, deltaY = 0;
            for (int i = 0; i < currentPoints.Count; i++)
            {
                deltaX += currentPoints[i].X - previousPoints[i].X;
                deltaY += currentPoints[i].Y - previousPoints[i].Y;
            }
            deltaX /= currentPoints.Count;
            deltaY /= currentPoints.Count;

            // 转换为像素/秒
            double velocityX = (deltaX / (double)deltaTimeMs) * 1000;
            double velocityY = (deltaY / (double)deltaTimeMs) * 1000;

            return new VelocityVector(velocityX, velocityY);
        }
    }
}
