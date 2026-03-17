using GestureSign.Common.Applications;
using GestureSign.Daemon.Triggers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Reflection;

namespace GestureSign.Tests
{
    /// <summary>
    /// 测试惯性继承速度逻辑：手指落下时根据方向和速度决定是否继承惯性剩余速度。
    ///
    /// 核心行为：
    ///   - 手指静止落下（magnitude &lt; MomentumStopThreshold）→ 惯性停止，重置方向状态
    ///   - 手指同向落下（magnitude &gt;= threshold &amp;&amp; 同向）→ 继承剩余惯性速度（取 max）
    ///   - 手指反向落下 → 惯性停止，重置方向状态
    ///   - 无惯性时手指落下 → 正常重置，无继承
    /// </summary>
    [TestClass]
    public class InertialScrollInheritanceTests
    {
        // ─── 反射辅助 ──────────────────────────────────────────────────────────

        private static T GetField<T>(InertialScrollExecutor ise, string name)
        {
            var field = typeof(InertialScrollExecutor)
                .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"Field '{name}' not found");
            return (T)field.GetValue(ise);
        }

        private static void SetField<T>(InertialScrollExecutor ise, string name, T value)
        {
            var field = typeof(InertialScrollExecutor)
                .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"Field '{name}' not found");
            field.SetValue(ise, value);
        }

        private static object GetDirectionState(InertialScrollExecutor ise)
            => GetField<object>(ise, "_directionState");

        private static string DirectionStateName(InertialScrollExecutor ise)
            => GetDirectionState(ise).ToString();

        // ─── 标准设置 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 构建测试用设置：关闭 NoiseRatio（Vertical 模式）避免方向状态机干扰 ProcessFrame 分支，
        /// 关闭 WinUI 检测避免 UIA 调用，使用 Vertical 方向避免触发 InputSimulator。
        /// </summary>
        private static InertialScrollSettings MakeSettings(
            double stopThreshold = 500.0,
            double tau = 600.0,
            double minVelocity = 10.0)
        {
            return new InertialScrollSettings
            {
                Direction = ScrollDirection.Vertical,   // 单轴：跳过方向状态机，跳过 X 轴 InputSimulator
                NoiseRatio = 0,                          // 不触发方向状态机
                EnableWinUIDetection = false,            // 不触发 UIA
                MomentumStopThreshold = stopThreshold,
                MomentumTimeConstantMs = tau,
                MomentumMinVelocity = minVelocity,
                PixelsPerScrollUnit = 1000,             // 极大值：_accumulatedY 不会真正发出滚动
                AccelerationFactor = 1.5,
                ReverseDirection = false,
                ReverseHorizontalDirection = false,
                EnableMomentum = true,
                MomentumMaxDurationMs = 5000,
            };
        }

        /// <summary>
        /// 构建带指定时间戳的速度向量（用于模拟时间间隔）。
        /// </summary>
        private static VelocityVector MakeVelocityAt(
            DateTime timestamp, double vy, double vx = 0,
            double deltaY = 5, double deltaX = 0)
        {
            var v = new VelocityVector(vx, vy, deltaX, deltaY);
            v.Timestamp = timestamp;
            return v;
        }

        /// <summary>
        /// 模拟惯性启动：手动设置 ISE 内部惯性状态字段，相当于 StartInertiaIfNeeded 执行后的结果。
        /// _inertiaStopwatch 被 Restart（elapsed≈0），对应刚启动惯性、decay≈1.0 的场景。
        /// 若需要模拟较大 elapsed，请在调用后用 Thread.Sleep，或直接选择合适的参数使 residual 足够大/小。
        /// </summary>
        private static void SimulateInertiaStarted(
            InertialScrollExecutor ise,
            double startVelocityY,
            double startVelocityX = 0)
        {
            SetField(ise, "_isInertiaActive", true);
            SetField(ise, "_inertiaStartVelocityX", startVelocityX);
            SetField(ise, "_inertiaStartVelocityY", startVelocityY);
            var sw = GetField<System.Diagnostics.Stopwatch>(ise, "_inertiaStopwatch");
            sw.Restart();
        }

        /// <summary>
        /// 设置 _lastGestureTime 为过去某时刻，使下一帧 timeSinceLastGesture > 200ms。
        /// </summary>
        private static DateTime SetLastGestureTimeInPast(InertialScrollExecutor ise, int msAgo = 300)
        {
            var t = DateTime.Now.AddMilliseconds(-msAgo);
            SetField(ise, "_lastGestureTime", t);
            return t;
        }

        // ──────────────────────────────────────────────────────────────────────
        // T1：手指静止落下 → 惯性停止，继承速度清零，方向状态重置
        // ──────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void ProcessFrame_WhenInertiaActive_StationaryFinger_ClearsInheritedVelocity()
        {
            var ise = new InertialScrollExecutor();
            var settings = MakeSettings(stopThreshold: 500);

            // 模拟惯性已启动（向下，启动速度 1000 px/s）
            SimulateInertiaStarted(ise, startVelocityY: 1000);
            SetLastGestureTimeInPast(ise, msAgo: 300);

            // 手指落下：速度 100 px/s < 500 threshold → 静止
            var v = MakeVelocityAt(DateTime.Now, vy: 100, deltaY: 1);
            ise.ProcessFrame(v, window: null, settings: settings);

            double inheritedVx = GetField<double>(ise, "_inheritedVelocityX");
            double inheritedVy = GetField<double>(ise, "_inheritedVelocityY");
            Assert.AreEqual(0.0, inheritedVx, 0.001, "静止落下：_inheritedVelocityX 应清零");
            Assert.AreEqual(0.0, inheritedVy, 0.001, "静止落下：_inheritedVelocityY 应清零");
        }

        [TestMethod]
        public void ProcessFrame_WhenInertiaActive_StationaryFinger_ResetsDirectionState()
        {
            var ise = new InertialScrollExecutor();
            var settings = new InertialScrollSettings
            {
                Direction = ScrollDirection.Both,
                NoiseRatio = 0.25,
                EnableWinUIDetection = false,
                MomentumStopThreshold = 500,
                MomentumTimeConstantMs = 600,
                MomentumMinVelocity = 10,
                PixelsPerScrollUnit = 1000,
                AccelerationFactor = 1.5,
            };

            SimulateInertiaStarted(ise, startVelocityY: 1000);
            // 手动把方向状态设为 LockY（模拟已锁轴）
            // 通过枚举反射设置
            var dirEnum = typeof(InertialScrollExecutor)
                .GetNestedType("DirectionState", BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("DirectionState enum not found");
            var lockYVal = Enum.Parse(dirEnum, "LockY");
            SetField(ise, "_directionState", lockYVal);

            SetLastGestureTimeInPast(ise, msAgo: 300);

            // 静止落下
            var v = MakeVelocityAt(DateTime.Now, vy: 100, deltaY: 1);
            ise.ProcessFrame(v, window: null, settings: settings);

            Assert.AreEqual("Undecided", DirectionStateName(ise), "静止落下：方向状态应重置为 Undecided");
        }

        // ──────────────────────────────────────────────────────────────────────
        // T2：手指同向落下（速度 > threshold）→ 继承剩余惯性速度（惯性残余 > 手指速度时）
        // ──────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void ProcessFrame_WhenInertiaActive_SameDirectionFinger_SetsInheritedVelocity_WhenResidualGreaterThanFinger()
        {
            var ise = new InertialScrollExecutor();
            // tau=600ms，惯性起始 1500 px/s
            // elapsedMs≈0（刚启动），decay≈1.0，residualVy≈1500 > 手指 600 → 设置继承
            var settings = MakeSettings(stopThreshold: 500, tau: 600, minVelocity: 10);

            SimulateInertiaStarted(ise, startVelocityY: 1500);
            SetLastGestureTimeInPast(ise, msAgo: 300);

            // 手指同向落下：速度 600 px/s > 500 threshold，同向（向下）
            var v = MakeVelocityAt(DateTime.Now, vy: 600, deltaY: 5);
            ise.ProcessFrame(v, window: null, settings: settings);

            double inheritedVy = GetField<double>(ise, "_inheritedVelocityY");
            Assert.IsTrue(inheritedVy > 0, $"同向落下且惯性剩余>{600}：_inheritedVelocityY 应 > 0，实际={inheritedVy:F1}");
            Assert.IsTrue(inheritedVy > 600, $"继承速度应 > 手指速度 600，实际={inheritedVy:F1}");
        }

        [TestMethod]
        public void ProcessFrame_WhenInertiaActive_SameDirectionFinger_NoInheritedVelocity_WhenFingerFasterThanResidual()
        {
            var ise = new InertialScrollExecutor();
            // 惯性起始 300 px/s（elapsed≈0，decay≈1.0，residualVy≈300）< 手指 700 → 继承速度=0
            var settings = MakeSettings(stopThreshold: 500, tau: 600, minVelocity: 10);

            SimulateInertiaStarted(ise, startVelocityY: 300);
            SetLastGestureTimeInPast(ise, msAgo: 300);

            // 手指同向落下：速度 700 px/s > 惯性剩余 300 → 直接用手指速度，继承速度=0
            var v = MakeVelocityAt(DateTime.Now, vy: 700, deltaY: 5);
            ise.ProcessFrame(v, window: null, settings: settings);

            double inheritedVy = GetField<double>(ise, "_inheritedVelocityY");
            Assert.AreEqual(0.0, inheritedVy, 0.001,
                "手指速度 > 惯性剩余：_inheritedVelocityY 应为 0（直接用手指速度，无需补偿）");
        }

        [TestMethod]
        public void ProcessFrame_WhenInertiaActive_SameDirectionFinger_PreservesDirectionState()
        {
            var ise = new InertialScrollExecutor();
            // 使用 Both + NoiseRatio 以测试方向状态保留
            var settings = new InertialScrollSettings
            {
                Direction = ScrollDirection.Both,
                NoiseRatio = 0.25,
                EnableWinUIDetection = false,
                MomentumStopThreshold = 500,
                MomentumTimeConstantMs = 600,
                MomentumMinVelocity = 10,
                PixelsPerScrollUnit = 1000,
                AccelerationFactor = 1.5,
                RelockRatioMultiplier = 0.8,
                LockExitMinorDistancePx = 6,
            };

            SimulateInertiaStarted(ise, startVelocityY: 1500);

            // 手动把方向状态设为 LockY（模拟手势末态锁轴）
            var dirEnum = typeof(InertialScrollExecutor)
                .GetNestedType("DirectionState", BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("DirectionState enum not found");
            var lockYVal = Enum.Parse(dirEnum, "LockY");
            SetField(ise, "_directionState", lockYVal);

            SetLastGestureTimeInPast(ise, msAgo: 300);

            // 手指同向落下（速度 700 > 500 threshold，向下）
            var v = MakeVelocityAt(DateTime.Now, vy: 700, deltaY: 5);
            ise.ProcessFrame(v, window: null, settings: settings);

            Assert.AreEqual("LockY", DirectionStateName(ise), "同向落下：方向状态应保留 LockY，不重置");
        }

        // ──────────────────────────────────────────────────────────────────────
        // T3：手指反向落下 → 继承速度清零，方向状态重置
        // ──────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void ProcessFrame_WhenInertiaActive_ReverseDirectionFinger_ClearsInheritedVelocity()
        {
            var ise = new InertialScrollExecutor();
            var settings = MakeSettings(stopThreshold: 500);

            // 惯性向下（startVelocityY = 1000 > 0 表示向下）
            SimulateInertiaStarted(ise, startVelocityY: 1000);
            SetLastGestureTimeInPast(ise, msAgo: 300);

            // 手指反向（向上，vy = -700）
            var v = MakeVelocityAt(DateTime.Now, vy: -700, deltaY: -5);
            ise.ProcessFrame(v, window: null, settings: settings);

            double inheritedVy = GetField<double>(ise, "_inheritedVelocityY");
            Assert.AreEqual(0.0, inheritedVy, 0.001, "反向落下：_inheritedVelocityY 应清零");
        }

        [TestMethod]
        public void ProcessFrame_WhenInertiaActive_ReverseDirectionFinger_ResetsDirectionState()
        {
            var ise = new InertialScrollExecutor();
            var settings = new InertialScrollSettings
            {
                Direction = ScrollDirection.Both,
                NoiseRatio = 0.25,
                EnableWinUIDetection = false,
                MomentumStopThreshold = 500,
                MomentumTimeConstantMs = 600,
                MomentumMinVelocity = 10,
                PixelsPerScrollUnit = 1000,
                AccelerationFactor = 1.5,
                RelockRatioMultiplier = 0.8,
                LockExitMinorDistancePx = 6,
            };

            SimulateInertiaStarted(ise, startVelocityY: 1000);

            var dirEnum = typeof(InertialScrollExecutor)
                .GetNestedType("DirectionState", BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("DirectionState enum not found");
            SetField(ise, "_directionState", Enum.Parse(dirEnum, "LockY"));

            SetLastGestureTimeInPast(ise, msAgo: 300);

            var v = MakeVelocityAt(DateTime.Now, vy: -700, deltaY: -5);
            ise.ProcessFrame(v, window: null, settings: settings);

            Assert.AreEqual("Undecided", DirectionStateName(ise), "反向落下：方向状态应重置为 Undecided");
        }

        // ──────────────────────────────────────────────────────────────────────
        // T4：无惯性时手指落下 → 正常重置，_inheritedVelocityX/Y 保持 0
        // ──────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void ProcessFrame_NoInertia_NewGesture_NeverSetsInheritedVelocity()
        {
            var ise = new InertialScrollExecutor();
            var settings = MakeSettings(stopThreshold: 500);

            // _isInertiaActive 默认 false，确保没有惯性
            SetLastGestureTimeInPast(ise, msAgo: 300);

            var v = MakeVelocityAt(DateTime.Now, vy: 800, deltaY: 5);
            ise.ProcessFrame(v, window: null, settings: settings);

            double inheritedVy = GetField<double>(ise, "_inheritedVelocityY");
            Assert.AreEqual(0.0, inheritedVy, 0.001, "无惯性新手势：不应设置继承速度");
        }

        // ──────────────────────────────────────────────────────────────────────
        // T5：继承速度按 tau 衰减（连续帧）
        // ──────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void ProcessFrame_InheritedVelocity_DecaysWithTau()
        {
            var ise = new InertialScrollExecutor();
            double tau = 600.0;
            var settings = MakeSettings(stopThreshold: 500, tau: tau, minVelocity: 1);

            // 启动惯性，让 residualVy >> 手指速度，确保继承速度 > 0
            SimulateInertiaStarted(ise, startVelocityY: 3000);
            SetLastGestureTimeInPast(ise, msAgo: 300);

            // 第一帧：手指落下，设置继承速度
            var t0 = DateTime.Now;
            var v0 = MakeVelocityAt(t0, vy: 600, deltaY: 5);
            ise.ProcessFrame(v0, window: null, settings: settings);

            double inheritedVyAfterFirst = GetField<double>(ise, "_inheritedVelocityY");
            Assert.IsTrue(inheritedVyAfterFirst > 0, $"第一帧后继承速度应 > 0，实际={inheritedVyAfterFirst:F1}");

            // 第二帧：50ms 后（在同一手势内，timeSinceLastGesture < 200ms）
            double dtMs = 50;
            var t1 = t0.AddMilliseconds(dtMs);
            // _lastGestureTime 已被第一帧更新为 t0，所以第二帧间隔 50ms < 200ms
            var v1 = MakeVelocityAt(t1, vy: 600, deltaY: 5);
            ise.ProcessFrame(v1, window: null, settings: settings);

            double inheritedVyAfterSecond = GetField<double>(ise, "_inheritedVelocityY");

            // 期望：inheritedVyAfterSecond ≈ inheritedVyAfterFirst * exp(-dtMs/tau)
            double expectedDecay = Math.Exp(-dtMs / tau);
            double expectedVy = inheritedVyAfterFirst * expectedDecay;
            Assert.AreEqual(expectedVy, inheritedVyAfterSecond, inheritedVyAfterFirst * 0.05,
                $"继承速度应按 tau={tau}ms 衰减，期望≈{expectedVy:F1}，实际={inheritedVyAfterSecond:F1}");
        }

        // ──────────────────────────────────────────────────────────────────────
        // T6：继承速度衰减到 MomentumMinVelocity 以下时清零
        // ──────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void ProcessFrame_InheritedVelocity_ClearedWhenBelowMinVelocity()
        {
            var ise = new InertialScrollExecutor();
            double tau = 600.0;
            double minVelocity = 50.0; // 较高的最小速度，方便触发清零
            var settings = MakeSettings(stopThreshold: 500, tau: tau, minVelocity: minVelocity);

            // 手动直接设置一个极小的继承速度（低于 minVelocity）
            SetField(ise, "_inheritedVelocityX", 0.0);
            SetField(ise, "_inheritedVelocityY", 30.0); // < minVelocity=50
            SetField(ise, "_inheritedVelocityTime", DateTime.Now.AddMilliseconds(-1));
            // _lastGestureTime 设为近期（让 timeSinceLastGesture < 200ms，进入正常帧处理路径）
            SetField(ise, "_lastGestureTime", DateTime.Now.AddMilliseconds(-10));

            var v = MakeVelocityAt(DateTime.Now, vy: 200, deltaY: 3);
            ise.ProcessFrame(v, window: null, settings: settings);

            double inheritedVy = GetField<double>(ise, "_inheritedVelocityY");
            // 继承速度衰减后仍远低于 minVelocity（30 * exp(-1/600) ≈ 29.9 < 50），应被清零
            Assert.AreEqual(0.0, inheritedVy, 0.001,
                $"继承速度 < MomentumMinVelocity({minVelocity}) 时应清零，实际={inheritedVy:F1}");
        }

        // ──────────────────────────────────────────────────────────────────────
        // T7：MomentumStopThreshold 精确边界
        // ──────────────────────────────────────────────────────────────────────

        [TestMethod]
        [DataRow(499.9, false, DisplayName = "magnitude=499.9 < threshold=500 → 静止路径")]
        [DataRow(500.1, true, DisplayName = "magnitude=500.1 > threshold=500 → 非静止路径")]
        public void ProcessFrame_StopThreshold_Boundary(double fingerVelocity, bool expectInherited)
        {
            var ise = new InertialScrollExecutor();
            var settings = MakeSettings(stopThreshold: 500, tau: 600, minVelocity: 1);

            // 惯性启动速度足够大，确保残余 > 手指速度
            SimulateInertiaStarted(ise, startVelocityY: 3000);
            SetLastGestureTimeInPast(ise, msAgo: 300);

            // 手指同向落下
            var v = MakeVelocityAt(DateTime.Now, vy: fingerVelocity, deltaY: fingerVelocity > 0 ? 3.0 : -3.0);
            ise.ProcessFrame(v, window: null, settings: settings);

            double inheritedVy = GetField<double>(ise, "_inheritedVelocityY");
            if (expectInherited)
                Assert.IsTrue(inheritedVy > 0,
                    $"magnitude={fingerVelocity} > threshold: 应继承，inheritedVy={inheritedVy:F1}");
            else
                Assert.AreEqual(0.0, inheritedVy, 0.001,
                    $"magnitude={fingerVelocity} < threshold: 应静止停止，inheritedVy={inheritedVy:F1}");
        }

        // ──────────────────────────────────────────────────────────────────────
        // T8：连续同向滚动中，继承速度不叠加（每次落下覆盖而非累积）
        // ──────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void ProcessFrame_SameDirectionConsecutiveTouches_InheritedVelocityNotAccumulated()
        {
            var ise = new InertialScrollExecutor();
            var settings = MakeSettings(stopThreshold: 200, tau: 600, minVelocity: 1);

            // 第一次惯性：启动速度 1000
            SimulateInertiaStarted(ise, startVelocityY: 1000);
            SetLastGestureTimeInPast(ise, msAgo: 300);

            // 第一次手指落下（同向 600 > 200 threshold），设置继承速度
            var t0 = DateTime.Now;
            var v0 = MakeVelocityAt(t0, vy: 600, deltaY: 5);
            ise.ProcessFrame(v0, window: null, settings: settings);
            double inheritedAfterFirst = GetField<double>(ise, "_inheritedVelocityY");

            // 模拟第二次惯性：再次设置惯性字段
            SetField(ise, "_isInertiaActive", true);
            SetField(ise, "_inertiaStartVelocityY", 900.0); // 第二次稍低
            var sw = GetField<System.Diagnostics.Stopwatch>(ise, "_inertiaStopwatch");
            sw.Restart();

            // 设置 _lastGestureTime 为 300ms 前，触发继承路径
            SetField(ise, "_lastGestureTime", DateTime.Now.AddMilliseconds(-300));

            // 第二次手指落下（同向 600）
            var t1 = DateTime.Now;
            var v1 = MakeVelocityAt(t1, vy: 600, deltaY: 5);
            ise.ProcessFrame(v1, window: null, settings: settings);
            double inheritedAfterSecond = GetField<double>(ise, "_inheritedVelocityY");

            // 期望：第二次继承速度 ≤ 第二次惯性起始速度，不会叠加第一次继承速度
            // 上限：第二次 _inertiaStartVelocityY = 900（decay≈1），所以 inheritedVy ≤ 900
            Assert.IsTrue(inheritedAfterSecond <= 900 + 1.0,
                $"连续触摸不应叠加继承速度：inheritedAfterSecond={inheritedAfterSecond:F1} 应 ≤ 900");
        }

        // ──────────────────────────────────────────────────────────────────────
        // T9：同一手势内连续帧（timeSinceLastGesture < 200ms）不重置状态
        // ──────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void ProcessFrame_ContinuousFrames_WithinThreshold_DoNotResetState()
        {
            var ise = new InertialScrollExecutor();
            var settings = MakeSettings(stopThreshold: 500);

            // 第一帧建立状态
            var t0 = DateTime.Now;
            var v0 = MakeVelocityAt(t0, vy: 600, deltaY: 5);
            ise.ProcessFrame(v0, window: null, settings: settings);

            // 手动设置继承速度（模拟已进入继承路径）
            SetField(ise, "_inheritedVelocityY", 800.0);
            SetField(ise, "_inheritedVelocityTime", t0);

            // 第二帧：50ms 后（< 200ms 阈值），不应触发重置路径
            var t1 = t0.AddMilliseconds(50);
            var v1 = MakeVelocityAt(t1, vy: 600, deltaY: 5);
            ise.ProcessFrame(v1, window: null, settings: settings);

            // 继承速度应该还在（衰减但不清零）
            double inheritedVy = GetField<double>(ise, "_inheritedVelocityY");
            Assert.IsTrue(inheritedVy > 0,
                $"同一手势内连续帧：继承速度不应清零（应衰减），实际={inheritedVy:F1}");
        }

        // ──────────────────────────────────────────────────────────────────────
        // T10：VelocityVector 默认构造函数 Timestamp 不为 MinValue（回归测试）
        // ──────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void VelocityVector_DefaultConstructor_TimestampIsNowNotMinValue()
        {
            var before = DateTime.Now;
            var v = new VelocityVector();
            var after = DateTime.Now;

            Assert.IsTrue(v.Timestamp >= before,
                $"VelocityVector() 默认构造 Timestamp 应 >= 创建前时间，实际={v.Timestamp}");
            Assert.IsTrue(v.Timestamp <= after,
                $"VelocityVector() 默认构造 Timestamp 应 <= 创建后时间，实际={v.Timestamp}");
            Assert.AreNotEqual(DateTime.MinValue, v.Timestamp,
                "VelocityVector() 默认构造 Timestamp 不应为 MinValue");
        }

        // ──────────────────────────────────────────────────────────────────────
        // T11：InertialScrollSettings 新字段默认值验证
        // ──────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void InertialScrollSettings_MomentumStopThreshold_DefaultIs500()
        {
            var settings = new InertialScrollSettings();
            Assert.AreEqual(500.0, settings.MomentumStopThreshold, 0.01,
                "MomentumStopThreshold 默认值应为 500.0");
        }

        // ──────────────────────────────────────────────────────────────────────
        // T12：无惯性 + 短间隔 → 保留方向状态（快速连续滑动优化）
        // ──────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void ProcessFrame_NoInertia_ShortInterval_PreservesDirectionState()
        {
            var ise = new InertialScrollExecutor();
            var settings = new InertialScrollSettings
            {
                Direction = ScrollDirection.Both,
                NoiseRatio = 0.25,
                EnableWinUIDetection = false,
                MomentumStopThreshold = 500,
                MomentumTimeConstantMs = 600,
                MomentumMinVelocity = 10,
                PixelsPerScrollUnit = 1000,
                AccelerationFactor = 1.5,
                RelockRatioMultiplier = 0.8,
                LockExitMinorDistancePx = 6,
            };

            // 手动设置方向状态为 LockY（模拟上一次手势已锁轴）
            var dirEnum = typeof(InertialScrollExecutor)
                .GetNestedType("DirectionState", BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("DirectionState enum not found");
            var lockYVal = Enum.Parse(dirEnum, "LockY");
            SetField(ise, "_directionState", lockYVal);

            // _isInertiaActive = false（无惯性），间隔 500ms（< 1500ms 继承阈值）
            SetLastGestureTimeInPast(ise, msAgo: 500);

            var v = MakeVelocityAt(DateTime.Now, vy: 800, deltaY: 5);
            ise.ProcessFrame(v, window: null, settings: settings);

            Assert.AreEqual("LockY", DirectionStateName(ise),
                "无惯性 + 短间隔（500ms < 1500ms）：方向状态应保留 LockY");
        }

        // ──────────────────────────────────────────────────────────────────────
        // T13：无惯性 + 长间隔 → 重置方向状态
        // ──────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void ProcessFrame_NoInertia_LongInterval_ResetsDirectionState()
        {
            var ise = new InertialScrollExecutor();
            var settings = new InertialScrollSettings
            {
                Direction = ScrollDirection.Both,
                NoiseRatio = 0.25,
                EnableWinUIDetection = false,
                MomentumStopThreshold = 500,
                MomentumTimeConstantMs = 600,
                MomentumMinVelocity = 10,
                PixelsPerScrollUnit = 1000,
                AccelerationFactor = 1.5,
                RelockRatioMultiplier = 0.8,
                LockExitMinorDistancePx = 6,
            };

            var dirEnum = typeof(InertialScrollExecutor)
                .GetNestedType("DirectionState", BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("DirectionState enum not found");
            var lockYVal = Enum.Parse(dirEnum, "LockY");
            SetField(ise, "_directionState", lockYVal);

            // 无惯性，间隔 2000ms（> 1500ms 继承阈值）
            SetLastGestureTimeInPast(ise, msAgo: 2000);

            var v = MakeVelocityAt(DateTime.Now, vy: 800, deltaY: 5);
            ise.ProcessFrame(v, window: null, settings: settings);

            Assert.AreEqual("Undecided", DirectionStateName(ise),
                "无惯性 + 长间隔（2000ms > 1500ms）：方向状态应重置为 Undecided");
        }

        // ──────────────────────────────────────────────────────────────────────
        // T14：ResetGestureState 不清零 _lastGestureTime
        // ──────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void ResetGestureState_PreservesLastGestureTime()
        {
            var ise = new InertialScrollExecutor();
            var settings = MakeSettings();

            // 先发送一帧建立 _lastGestureTime
            var t0 = DateTime.Now;
            var v0 = MakeVelocityAt(t0, vy: 600, deltaY: 5);
            ise.ProcessFrame(v0, window: null, settings: settings);

            var lastGestureTimeBefore = GetField<DateTime>(ise, "_lastGestureTime");
            Assert.AreNotEqual(DateTime.MinValue, lastGestureTimeBefore,
                "ProcessFrame 后 _lastGestureTime 不应为 MinValue");

            // 调用 ResetGestureState
            ise.ResetGestureState();

            var lastGestureTimeAfter = GetField<DateTime>(ise, "_lastGestureTime");
            Assert.AreEqual(lastGestureTimeBefore, lastGestureTimeAfter,
                "ResetGestureState 不应清零 _lastGestureTime");
        }
    }
}
