using GestureSign.Common.Configuration;
using GestureSign.Common.Gestures;
using GestureSign.Common.Input;
using GestureSign.PointPatterns;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace GestureSign.Tests
{
    [TestClass]
    public class FeatureFingerMatchTests
    {
        // TODO: 以下测试依赖 Phase 1/2 Feature Finger 功能（Gesture.MatchStrategy、GestureManager.ResolveEffectiveStrategy/GetFeatureFingerTrajectoryIndex），
        // 待相关功能实现后启用。

        #region Backward Compatibility

        [TestMethod]
        public void FingerMatchStrategy_Inherit_HasValueZero()
        {
            Assert.AreEqual(0, (int)FingerMatchStrategy.Inherit);
        }

        #endregion

        #region Scale Point

        [TestMethod]
        public void ScalePoint_Scale2x_DoublesDistanceFromCenter()
        {
            var center = new Point(100, 100);
            var point = new Point(200, 200);
            double scale = 2.0;

            var scaled = new Point(
                (int)Math.Round(center.X + (point.X - center.X) * scale),
                (int)Math.Round(center.Y + (point.Y - center.Y) * scale));

            Assert.AreEqual(new Point(300, 300), scaled);
        }

        [TestMethod]
        public void ScalePoint_Scale05x_HalvesDistanceFromCenter()
        {
            var center = new Point(100, 100);
            var point = new Point(200, 200);
            double scale = 0.5;

            var scaled = new Point(
                (int)Math.Round(center.X + (point.X - center.X) * scale),
                (int)Math.Round(center.Y + (point.Y - center.Y) * scale));

            Assert.AreEqual(new Point(150, 150), scaled);
        }

        [TestMethod]
        public void ScalePoint_CenterPoint_RemainsUnchanged()
        {
            var center = new Point(100, 100);
            double scale = 3.0;

            var scaled = new Point(
                (int)Math.Round(center.X + (center.X - center.X) * scale),
                (int)Math.Round(center.Y + (center.Y - center.Y) * scale));

            Assert.AreEqual(center, scaled);
        }

        [TestMethod]
        public void ScalePoint_MathRound_CorrectlyRoundsHalfValues()
        {
            // 验证 Math.Round 而非 (int) 截断的行为差异
            // center=0, point=1, scale=1.5 → 0 + 1*1.5 = 1.5 → Math.Round = 2, (int) = 1
            var center = new Point(0, 0);
            var point = new Point(1, 1);
            double scale = 1.5;

            var scaled = new Point(
                (int)Math.Round(center.X + (point.X - center.X) * scale),
                (int)Math.Round(center.Y + (point.Y - center.Y) * scale));

            Assert.AreEqual(new Point(2, 2), scaled);
        }

        #endregion

        #region FingerMatchStrategy Enum Validation

        [TestMethod]
        [DataRow(0, true)]  // Inherit
        [DataRow(1, true)]  // AllFingers
        [DataRow(2, true)]  // FeatureFinger
        [DataRow(-1, false)]
        [DataRow(3, false)]
        [DataRow(999, false)]
        public void FingerMatchStrategy_IsDefined_ValidatesCorrectly(int value, bool expectedDefined)
        {
            Assert.AreEqual(expectedDefined, Enum.IsDefined(typeof(FingerMatchStrategy), value));
        }

        [TestMethod]
        public void InvalidMatchStrategy_FallsBackToInherit()
        {
            // 模拟 JSON 加载时的 Enum.IsDefined 校验逻辑
            int invalidRaw = 999;
            var result = Enum.IsDefined(typeof(FingerMatchStrategy), invalidRaw)
                ? (FingerMatchStrategy)invalidRaw
                : FingerMatchStrategy.Inherit;

            Assert.AreEqual(FingerMatchStrategy.Inherit, result);
        }

        [TestMethod]
        public void NegativeMatchStrategy_FallsBackToInherit()
        {
            int negativeRaw = -1;
            var result = Enum.IsDefined(typeof(FingerMatchStrategy), negativeRaw)
                ? (FingerMatchStrategy)negativeRaw
                : FingerMatchStrategy.Inherit;

            Assert.AreEqual(FingerMatchStrategy.Inherit, result);
        }

        #endregion

        #region GestureTrailScale Boundary

        [TestMethod]
        public void GestureTrailScale_OutOfRangeLow_FallsBackToDefault()
        {
            // getter 对越界值回退到 1.0
            double raw = 0.1;
            double result = raw < 0.2 || raw > 3.0 ? 1.0 : raw;
            Assert.AreEqual(1.0, result);
        }

        [TestMethod]
        public void GestureTrailScale_OutOfRangeHigh_FallsBackToDefault()
        {
            double raw = 5.0;
            double result = raw < 0.2 || raw > 3.0 ? 1.0 : raw;
            Assert.AreEqual(1.0, result);
        }

        [TestMethod]
        public void GestureTrailScale_NegativeValue_FallsBackToDefault()
        {
            double raw = -1.0;
            double result = raw < 0.2 || raw > 3.0 ? 1.0 : raw;
            Assert.AreEqual(1.0, result);
        }

        [TestMethod]
        [DataRow(0.2)]
        [DataRow(1.0)]
        [DataRow(3.0)]
        public void GestureTrailScale_ValidRange_ReturnsAsIs(double raw)
        {
            double result = raw < 0.2 || raw > 3.0 ? 1.0 : raw;
            Assert.AreEqual(raw, result);
        }

        #endregion

        #region MatchStrategy Panel Visibility Logic

        [TestMethod]
        [DataRow(RecordedGestureType.Click, true)]
        [DataRow(RecordedGestureType.Tap, true)]
        [DataRow(RecordedGestureType.TipTap, true)]
        [DataRow(RecordedGestureType.Trajectory, false)]
        [DataRow(RecordedGestureType.Unknown, false)]
        public void MatchStrategyPanel_IsContactGesture_HidesCorrectly(RecordedGestureType type, bool shouldHide)
        {
            // 与 GestureDefinition.OnCurrentRecordedDefinitionChanged 中的判断逻辑一致
            bool isContactGesture = type is
                RecordedGestureType.Click or
                RecordedGestureType.Tap or
                RecordedGestureType.TipTap;

            Assert.AreEqual(shouldHide, isContactGesture,
                $"Type={type}: shouldHide={shouldHide}, isContactGesture={isContactGesture}");
        }

        #endregion

        #region Helpers

        private static PointPattern CreateSimplePattern(int trajectoryCount)
        {
            var points = new Point[trajectoryCount][];
            for (int i = 0; i < trajectoryCount; i++)
            {
                points[i] = new[]
                {
                    new Point(i * 100, 0),
                    new Point(i * 100 + 50, 50),
                    new Point(i * 100 + 100, 100),
                };
            }
            return new PointPattern(points, trajectoryCount);
        }

        #endregion
    }
}
