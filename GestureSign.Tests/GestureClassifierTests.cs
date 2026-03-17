using GestureSign.Common.Input;
using GestureSign.Common.Applications;
using GestureSign.Daemon.Input;
using ManagedWinapi.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace GestureSign.Tests
{
    [TestClass]
    public class GestureClassifierTests
    {
        [TestMethod]
        public void Classify_PrimaryButtonClick_ReturnsClick()
        {
            var sample = new RecordedGestureSample
            {
                FingerCount = 2,
                DurationMs = 80,
                Analysis = new GestureAnalysis { IsTapLike = false, MaxPerFingerDistance = 5 },
                Session = new GestureSessionSnapshot
                {
                    FingerCount = 2,
                    HasPrimaryButtonClick = true,
                    PrimaryButtonFingerCount = 2,
                    PrimaryButtonDownTimeMs = 10,
                    PrimaryButtonUpTimeMs = 70,
                    ContactTrajectories = new Dictionary<int, List<Point>>
                    {
                        [1] = new() { new Point(100, 200), new Point(101, 200) },
                        [2] = new() { new Point(200, 200), new Point(201, 201) },
                    }
                }
            };

            var type = GestureClassifier.Classify(sample);

            Assert.AreEqual(RecordedGestureType.Click, type);
        }

        [TestMethod]
        public void Classify_TapLikeMultiFinger_ReturnsTap()
        {
            var sample = new RecordedGestureSample
            {
                FingerCount = 3,
                DurationMs = 120,
                Analysis = new GestureAnalysis { IsTapLike = true },
                Session = new GestureSessionSnapshot
                {
                    ContactTrajectories = new Dictionary<int, List<Point>>
                    {
                        [1] = new() { new Point(10, 10), new Point(12, 10) },
                        [2] = new() { new Point(30, 10), new Point(31, 11) },
                        [3] = new() { new Point(50, 10), new Point(52, 11) },
                    }
                }
            };

            var type = GestureClassifier.Classify(sample);

            Assert.AreEqual(RecordedGestureType.Tap, type);
        }

        [TestMethod]
        public void Classify_WithoutTimingMetadata_FallsBackToTap()
        {
            var sample = new RecordedGestureSample
            {
                FingerCount = 2,
                DurationMs = 120,
                Analysis = new GestureAnalysis { IsTapLike = true },
                Session = new GestureSessionSnapshot
                {
                    ContactTrajectories = new Dictionary<int, List<Point>>
                    {
                        [1] = new() { new Point(200, 200), new Point(201, 200) },
                        [2] = new() { new Point(120, 200), new Point(160, 205) },
                    }
                }
            };

            var type = GestureClassifier.Classify(sample);

            Assert.AreEqual(RecordedGestureType.Tap, type);
        }

        [TestMethod]
        public void CreateTipTapDefinition_DetectsLeftDirection()
        {
            var sample = new RecordedGestureSample
            {
                FingerCount = 2,
                DurationMs = 120,
                Analysis = new GestureAnalysis { IsTapLike = true },
                Session = new GestureSessionSnapshot
                {
                    ContactTrajectories = new Dictionary<int, List<Point>>
                    {
                        [1] = new() { new Point(200, 200), new Point(201, 200) },
                        [2] = new() { new Point(120, 200), new Point(150, 205) },
                    }
                }
            };

            var config = GestureClassifier.CreateTipTapDefinition(sample);

            Assert.AreEqual(ContactGestureDirection.Left, config.Direction);
            Assert.AreEqual(1, config.FixFingerCount);
        }

        [TestMethod]
        public void CreateTipTapDefinition_DetectsRightDirection()
        {
            var sample = new RecordedGestureSample
            {
                FingerCount = 2,
                DurationMs = 120,
                Analysis = new GestureAnalysis { IsTapLike = true },
                Session = new GestureSessionSnapshot
                {
                    ContactTrajectories = new Dictionary<int, List<Point>>
                    {
                        [1] = new() { new Point(120, 200), new Point(121, 200) },
                        [2] = new() { new Point(240, 200), new Point(260, 205) },
                    }
                }
            };

            var config = GestureClassifier.CreateTipTapDefinition(sample);

            Assert.AreEqual(ContactGestureDirection.Right, config.Direction);
        }

        [TestMethod]
        public void CreateTipTapDefinition_DetectsMiddleDirectionInsideDeadzone()
        {
            var sample = new RecordedGestureSample
            {
                FingerCount = 2,
                DurationMs = 120,
                Analysis = new GestureAnalysis { IsTapLike = true },
                Session = new GestureSessionSnapshot
                {
                    ContactTrajectories = new Dictionary<int, List<Point>>
                    {
                        [1] = new() { new Point(200, 200), new Point(201, 200) },
                        [2] = new() { new Point(210, 200), new Point(216, 205) },
                    }
                }
            };

            var config = GestureClassifier.CreateTipTapDefinition(sample);

            Assert.AreEqual(ContactGestureDirection.Middle, config.Direction);
        }

        [TestMethod]
        public void CreateTipTapDefinition_DetectsUpDirection()
        {
            var sample = new RecordedGestureSample
            {
                FingerCount = 2,
                DurationMs = 120,
                Analysis = new GestureAnalysis { IsTapLike = true },
                Session = new GestureSessionSnapshot
                {
                    ContactTrajectories = new Dictionary<int, List<Point>>
                    {
                        [1] = new() { new Point(200, 200), new Point(201, 200) },
                        [2] = new() { new Point(202, 140), new Point(202, 130) },
                    }
                }
            };

            var config = GestureClassifier.CreateTipTapDefinition(sample);

            Assert.AreEqual(ContactGestureDirection.Middle, config.Direction,
                "垂直偏移不影响方向判定，水平无偏移时应为 Middle");
        }

        [TestMethod]
        public void CreateTipTapDefinition_DetectsMiddleWhenOnlyVerticalOffset()
        {
            var sample = new RecordedGestureSample
            {
                FingerCount = 2,
                DurationMs = 120,
                Analysis = new GestureAnalysis { IsTapLike = true },
                Session = new GestureSessionSnapshot
                {
                    ContactTrajectories = new Dictionary<int, List<Point>>
                    {
                        [1] = new() { new Point(200, 200), new Point(201, 200) },
                        [2] = new() { new Point(202, 250), new Point(202, 262) },
                    }
                }
            };

            var config = GestureClassifier.CreateTipTapDefinition(sample);

            Assert.AreEqual(ContactGestureDirection.Middle, config.Direction,
                "垂直偏移不影响方向判定，水平无偏移时应为 Middle");
        }

        [TestMethod]
        public void Classify_ThreeFingerTipTapWithShorterTapTrajectory_ReturnsTipTap()
        {
            var sample = new RecordedGestureSample
            {
                FingerCount = 3,
                DurationMs = 120,
                Analysis = new GestureAnalysis { IsTapLike = false, MaxPerFingerDistance = 24 },
                Session = new GestureSessionSnapshot
                {
                    ContactTrajectories = new Dictionary<int, List<Point>>
                    {
                        [1] = new() { new Point(180, 200), new Point(181, 200), new Point(181, 201), new Point(182, 201) },
                        [2] = new() { new Point(220, 200), new Point(220, 201), new Point(221, 201), new Point(221, 202) },
                        [3] = new() { new Point(120, 200), new Point(121, 200) },
                    }
                }
            };

            var type = GestureClassifier.Classify(sample);

            Assert.AreEqual(RecordedGestureType.TipTap, type);
        }

        [TestMethod]
        public void Classify_ThreeFingerTipTapWithLongHoldDuration_StillReturnsTipTap()
        {
            var sample = new RecordedGestureSample
            {
                FingerCount = 3,
                DurationMs = 520,
                Analysis = new GestureAnalysis { IsTapLike = false, MaxPerFingerDistance = 10 },
                Session = new GestureSessionSnapshot
                {
                    ContactTrajectories = new Dictionary<int, List<Point>>
                    {
                        [1] = new() { new Point(180, 200), new Point(181, 200), new Point(181, 201), new Point(182, 201), new Point(182, 201) },
                        [2] = new() { new Point(220, 200), new Point(220, 201), new Point(221, 201), new Point(221, 202), new Point(221, 202) },
                        [3] = new() { new Point(120, 200), new Point(121, 200) },
                    }
                }
            };

            var type = GestureClassifier.Classify(sample);

            Assert.AreEqual(RecordedGestureType.TipTap, type);
        }

        [TestMethod]
        public void CreateTipTapDefinition_ThreeFingerSampleDetectsLeftFromShorterTapTrajectory()
        {
            var sample = new RecordedGestureSample
            {
                FingerCount = 3,
                DurationMs = 120,
                Analysis = new GestureAnalysis { IsTapLike = false, MaxPerFingerDistance = 24 },
                Session = new GestureSessionSnapshot
                {
                    ContactTrajectories = new Dictionary<int, List<Point>>
                    {
                        [1] = new() { new Point(180, 200), new Point(181, 200), new Point(181, 201), new Point(182, 201) },
                        [2] = new() { new Point(220, 200), new Point(220, 201), new Point(221, 201), new Point(221, 202) },
                        [3] = new() { new Point(120, 200), new Point(121, 200) },
                    }
                }
            };

            var config = GestureClassifier.CreateTipTapDefinition(sample);

            Assert.AreEqual(2, config.FixFingerCount);
            Assert.AreEqual(ContactGestureDirection.Left, config.Direction);
        }

        [TestMethod]
        public void Classify_ThreeFingerTipTapFromExplicitTiming_ReturnsTipTap()
        {
            var sample = new RecordedGestureSample
            {
                FingerCount = 3,
                DurationMs = 420,
                Analysis = new GestureAnalysis { IsTapLike = false, MaxPerFingerDistance = 10 },
                Session = new GestureSessionSnapshot
                {
                    ContactDownOrder = new List<int> { 11, 12, 13 },
                    ContactUpOrder = new List<int> { 13, 11, 12 },
                    ContactDownTimesMs = new Dictionary<int, double> { [11] = 0, [12] = 5, [13] = 280 },
                    ContactUpTimesMs = new Dictionary<int, double> { [13] = 360, [11] = 420, [12] = 420 },
                    ContactTrajectories = new Dictionary<int, List<Point>>
                    {
                        [11] = new() { new Point(180, 200), new Point(181, 200) },
                        [12] = new() { new Point(220, 200), new Point(221, 200) },
                        [13] = new() { new Point(120, 200), new Point(121, 200) },
                    }
                }
            };

            var type = GestureClassifier.Classify(sample);

            Assert.AreEqual(RecordedGestureType.TipTap, type);
        }

        [TestMethod]
        public void Classify_TwoFingerTipTapFromDiagnosticSample_WithSystemDefaultTimeout_ReturnsTipTap()
        {
            var sample = new RecordedGestureSample
            {
                FingerCount = 2,
                DurationMs = 756.181,
                Analysis = new GestureAnalysis
                {
                    IsTapLike = false,
                    AveragePerFingerDistance = 0,
                    MaxPerFingerDistance = 0,
                },
                Session = new GestureSessionSnapshot
                {
                    FingerCount = 2,
                    DurationMs = 756.194,
                    ContactDownOrder = new List<int> { 0, 1 },
                    ContactUpOrder = new List<int> { 1, 0 },
                    ContactDownTimesMs = new Dictionary<int, double> { [0] = 0.003, [1] = 328.16 },
                    ContactUpTimesMs = new Dictionary<int, double> { [1] = 477.934, [0] = 756.183 },
                    ContactTrajectories = new Dictionary<int, List<Point>>
                    {
                        [0] = new() { new Point(1439, 743) },
                        [1] = new() { new Point(1931, 109) },
                    }
                }
            };

            var type = GestureClassifier.Classify(sample);

            Assert.AreEqual(RecordedGestureType.TipTap, type);
        }

        [TestMethod]
        public void Classify_ThreeFingerTipTapWithoutExplicitTapUp_ButOtherFingerReleasedFirst_DoesNotReturnTipTap()
        {
            var sample = new RecordedGestureSample
            {
                FingerCount = 3,
                DurationMs = 896,
                Analysis = new GestureAnalysis { IsTapLike = false, MaxPerFingerDistance = 33.108 },
                Session = new GestureSessionSnapshot
                {
                    ContactDownOrder = new List<int> { 0, 1, 2 },
                    ContactUpOrder = new List<int> { 0 },
                    ContactDownTimesMs = new Dictionary<int, double> { [0] = 529.175, [1] = 529.204, [2] = 807.897 },
                    ContactUpTimesMs = new Dictionary<int, double> { [0] = 0.004 },
                    ContactTrajectories = new Dictionary<int, List<Point>>
                    {
                        [0] = new() { new Point(1310, 501), new Point(1309, 491) },
                        [1] = new() { new Point(1785, 208), new Point(1785, 219) },
                        [2] = new() { new Point(2151, -271), new Point(2150, -260), new Point(2151, -268), new Point(2151, -282) },
                    }
                }
            };

            var type = GestureClassifier.Classify(sample);

            Assert.AreEqual(RecordedGestureType.Trajectory, type);
        }

        [TestMethod]
        public void CreateTipTapDefinition_NoisyTapSampleExpandsTapMovementThreshold()
        {
            var sample = new RecordedGestureSample
            {
                FingerCount = 3,
                DurationMs = 896,
                Analysis = new GestureAnalysis { IsTapLike = false, MaxPerFingerDistance = 33.108 },
                Session = new GestureSessionSnapshot
                {
                    ContactDownOrder = new List<int> { 0, 1, 2 },
                    ContactUpOrder = new List<int> { 0 },
                    ContactDownTimesMs = new Dictionary<int, double> { [0] = 529.175, [1] = 529.204, [2] = 807.897 },
                    ContactTrajectories = new Dictionary<int, List<Point>>
                    {
                        [0] = new() { new Point(1310, 501), new Point(1309, 491) },
                        [1] = new() { new Point(1785, 208), new Point(1785, 219) },
                        [2] = new() { new Point(2151, -271), new Point(2150, -260), new Point(2151, -268), new Point(2151, -282) },
                    }
                }
            };

            var config = GestureClassifier.CreateTipTapDefinition(sample);

            // Tap finger (contact 2, X≈2151) is to the right of fix fingers
            // (contacts 0 X≈1310, 1 X≈1785), so direction is Right.
            Assert.AreEqual(ContactGestureDirection.Right, config.Direction);
            Assert.IsTrue(config.Recognition.TapMaxMovementPx >= 33.108);
        }

        [TestMethod]
        public void Classify_ThreeFingerTipTapWithJitteryFixFingers_ReturnsTipTap()
        {
            var sample = new RecordedGestureSample
            {
                FingerCount = 3,
                DurationMs = 341.776,
                Analysis = new GestureAnalysis { IsTapLike = false, AveragePerFingerDistance = 13.044, MaxPerFingerDistance = 15.556 },
                Session = new GestureSessionSnapshot
                {
                    ContactDownOrder = new List<int> { 0, 1, 2 },
                    ContactUpOrder = new List<int> { 2 },
                    ContactDownTimesMs = new Dictionary<int, double> { [0] = 0.002, [1] = 4.492, [2] = 197.112 },
                    ContactUpTimesMs = new Dictionary<int, double> { [2] = 332.608 },
                    ContactTrajectories = new Dictionary<int, List<Point>>
                    {
                        [0] = new() { new Point(1813, 269), new Point(1812, 280) },
                        [1] = new() { new Point(1320, 564), new Point(1326, 570), new Point(1327, 563) },
                        [2] = new() { new Point(2290, -205), new Point(2284, -194) },
                    }
                }
            };

            var type = GestureClassifier.Classify(sample);

            Assert.AreEqual(RecordedGestureType.TipTap, type);
        }

        [TestMethod]
        public void Classify_ThreeFingerSimultaneousTap_DoesNotReturnTipTap()
        {
            var sample = new RecordedGestureSample
            {
                FingerCount = 3,
                DurationMs = 102,
                Analysis = new GestureAnalysis { IsTapLike = true, AveragePerFingerDistance = 0, MaxPerFingerDistance = 0 },
                Session = new GestureSessionSnapshot
                {
                    ContactDownOrder = new List<int> { 0, 1, 2 },
                    ContactUpOrder = new List<int> { 2 },
                    ContactDownTimesMs = new Dictionary<int, double> { [0] = 0.001, [1] = 0.001, [2] = 7.509 },
                    ContactUpTimesMs = new Dictionary<int, double> { [2] = 94.188 },
                    ContactTrajectories = new Dictionary<int, List<Point>>
                    {
                        [0] = new() { new Point(1681, 226) },
                        [1] = new() { new Point(818, 960) },
                        [2] = new() { new Point(1319, 510) },
                    }
                }
            };

            var type = GestureClassifier.Classify(sample);

            Assert.AreEqual(RecordedGestureType.Tap, type);
        }

        [TestMethod]
        public void Classify_TapFirst_WhenTapAndTipTapHeuristicsBothMatch_ReturnsTap()
        {
            var sample = new RecordedGestureSample
            {
                FingerCount = 3,
                DurationMs = 120,
                Analysis = new GestureAnalysis { IsTapLike = true, MaxPerFingerDistance = 10 },
                Session = new GestureSessionSnapshot
                {
                    ContactDownOrder = new List<int> { 11, 12, 13 },
                    ContactUpOrder = new List<int> { 13, 11, 12 },
                    ContactDownTimesMs = new Dictionary<int, double> { [11] = 0, [12] = 5, [13] = 60 },
                    ContactUpTimesMs = new Dictionary<int, double> { [13] = 90, [11] = 115, [12] = 120 },
                    ContactTrajectories = new Dictionary<int, List<Point>>
                    {
                        [11] = new() { new Point(180, 200), new Point(181, 200) },
                        [12] = new() { new Point(220, 200), new Point(221, 200) },
                        [13] = new() { new Point(120, 200), new Point(121, 200) },
                    }
                }
            };

            var type = GestureClassifier.Classify(sample);

            Assert.AreEqual(RecordedGestureType.Tap, type);
        }

        [TestMethod]
        public void Classify_ThreeFingerTipTap_WhenFixFingersRemainActive_ReturnsTipTap()
        {
            var sample = new RecordedGestureSample
            {
                FingerCount = 3,
                DurationMs = 160,
                Analysis = new GestureAnalysis { IsTapLike = false, AveragePerFingerDistance = 7.2, MaxPerFingerDistance = 21.6 },
                Session = new GestureSessionSnapshot
                {
                    ActiveContactIds = new List<int> { 0, 1 },
                    ContactDownOrder = new List<int> { 0, 1, 2 },
                    ContactUpOrder = new List<int> { 2 },
                    ContactDownTimesMs = new Dictionary<int, double> { [0] = 0.005, [1] = 0.008, [2] = 82.000 },
                    ContactUpTimesMs = new Dictionary<int, double> { [2] = 124.000 },
                    ContactTrajectories = new Dictionary<int, List<Point>>
                    {
                        [0] = new() { new Point(1803, 199) },
                        [1] = new() { new Point(1001, 1168) },
                        [2] = new() { new Point(1349, 504), new Point(1351, 494), new Point(1348, 483) },
                    }
                }
            };

            var type = GestureClassifier.Classify(sample);

            Assert.AreEqual(RecordedGestureType.TipTap, type);
        }

        [TestMethod]
        public void Classify_ThreeFingerTipTap_WhenTapUpTimingIsMissing_UsesActiveFixFallback()
        {
            var sample = new RecordedGestureSample
            {
                FingerCount = 3,
                DurationMs = 160,
                Analysis = new GestureAnalysis { IsTapLike = false, AveragePerFingerDistance = 7.2, MaxPerFingerDistance = 21.6 },
                Session = new GestureSessionSnapshot
                {
                    ActiveContactIds = new List<int> { 0, 1 },
                    ContactDownOrder = new List<int> { 0, 1, 2 },
                    ContactUpOrder = new List<int> { 2 },
                    ContactDownTimesMs = new Dictionary<int, double> { [0] = 0.005, [1] = 0.008, [2] = 82.000 },
                    ContactTrajectories = new Dictionary<int, List<Point>>
                    {
                        [0] = new() { new Point(1803, 199) },
                        [1] = new() { new Point(1001, 1168) },
                        [2] = new() { new Point(1349, 504), new Point(1351, 494), new Point(1348, 483) },
                    }
                }
            };

            var type = GestureClassifier.Classify(sample);

            Assert.AreEqual(RecordedGestureType.TipTap, type);
        }

        [TestMethod]
        public void Classify_SimultaneousThreeFingerTapWithoutFullRelease_ReturnsTrajectory()
        {
            int oldFixHoldMs = GestureSign.Common.Configuration.AppConfig.TipTapFixMinHoldMs;

            try
            {
                GestureSign.Common.Configuration.AppConfig.TipTapFixMinHoldMs = 50;

            var sample = new RecordedGestureSample
            {
                FingerCount = 3,
                DurationMs = 71.166,
                Analysis = new GestureAnalysis { IsTapLike = true, AveragePerFingerDistance = 0, MaxPerFingerDistance = 0 },
                Session = new GestureSessionSnapshot
                {
                    ActiveContactIds = new List<int> { 0, 1 },
                    ContactDownOrder = new List<int> { 0, 1, 2 },
                    ContactUpOrder = new List<int> { 2 },
                    ContactDownTimesMs = new Dictionary<int, double> { [0] = 0.002, [1] = 0.004, [2] = 16.326 },
                    ContactUpTimesMs = new Dictionary<int, double> { [2] = 71.168 },
                    ContactTrajectories = new Dictionary<int, List<Point>>
                    {
                        [0] = new() { new Point(1391, 518) },
                        [1] = new() { new Point(930, 1072) },
                        [2] = new() { new Point(1715, 145) },
                    }
                }
            };

            var type = GestureClassifier.Classify(sample);

            Assert.AreEqual(RecordedGestureType.Trajectory, type);
            }
            finally
            {
                GestureSign.Common.Configuration.AppConfig.TipTapFixMinHoldMs = oldFixHoldMs;
            }
        }

        [TestMethod]
        public void CreateTipTapDefinition_WhenTapIsBetweenFixFingers_ReturnsMiddle()
        {
            var sample = new RecordedGestureSample
            {
                FingerCount = 3,
                DurationMs = 160,
                Analysis = new GestureAnalysis { IsTapLike = false, AveragePerFingerDistance = 7.2, MaxPerFingerDistance = 21.6 },
                Session = new GestureSessionSnapshot
                {
                    ActiveContactIds = new List<int> { 0, 1 },
                    ContactDownOrder = new List<int> { 0, 1, 2 },
                    ContactUpOrder = new List<int> { 2 },
                    ContactDownTimesMs = new Dictionary<int, double> { [0] = 0.005, [1] = 0.008, [2] = 82.000 },
                    ContactUpTimesMs = new Dictionary<int, double> { [2] = 124.000 },
                    ContactTrajectories = new Dictionary<int, List<Point>>
                    {
                        [0] = new() { new Point(1803, 199) },
                        [1] = new() { new Point(1001, 1168) },
                        [2] = new() { new Point(1349, 504), new Point(1351, 494), new Point(1348, 483) },
                    }
                }
            };

            var config = GestureClassifier.CreateTipTapDefinition(sample);

            Assert.AreEqual(ContactGestureDirection.Middle, config.Direction);
        }

        [TestMethod]
        public void Classify_RolledThreeFingerTapWithTwoRemainingActive_DoesNotReturnTipTap()
        {
            var sample = new RecordedGestureSample
            {
                FingerCount = 3,
                DurationMs = 71.888,
                Analysis = new GestureAnalysis { IsTapLike = false, AveragePerFingerDistance = 17.65, MaxPerFingerDistance = 27.587 },
                Session = new GestureSessionSnapshot
                {
                    ActiveContactIds = new List<int> { 1, 2 },
                    ContactDownOrder = new List<int> { 0, 1, 2 },
                    ContactUpOrder = new List<int> { 0 },
                    ContactDownTimesMs = new Dictionary<int, double> { [0] = 0.003, [1] = 0.005, [2] = 24.007 },
                    ContactUpTimesMs = new Dictionary<int, double> { [0] = 71.89 },
                    ContactTrajectories = new Dictionary<int, List<Point>>
                    {
                        [0] = new() { new Point(1674, 170), new Point(1677, 156) },
                        [1] = new() { new Point(1368, 518), new Point(1363, 497), new Point(1357, 497) },
                        [2] = new() { new Point(827, 955), new Point(828, 966) },
                    }
                }
            };

            var type = GestureClassifier.Classify(sample);

            Assert.AreEqual(RecordedGestureType.Trajectory, type);
        }

        [TestMethod]
        public void Classify_LastDownFallback_WhenExplicitUpOrderContradictsTapFirst_DoesNotReturnTipTap()
        {
            var sample = new RecordedGestureSample
            {
                FingerCount = 3,
                DurationMs = 158.635,
                Analysis = new GestureAnalysis { IsTapLike = false, AveragePerFingerDistance = 27.749, MaxPerFingerDistance = 45.921, DirectionVariance = 1.01 },
                Session = new GestureSessionSnapshot
                {
                    ActiveContactIds = new List<int> { 1 },
                    ContactDownOrder = new List<int> { 0, 1, 2 },
                    ContactUpOrder = new List<int> { 0, 2 },
                    ContactDownTimesMs = new Dictionary<int, double> { [0] = 0.005, [1] = 8.712, [2] = 112.379 },
                    ContactUpTimesMs = new Dictionary<int, double> { [0] = 151.749, [2] = 158.64 },
                    ContactTrajectories = new Dictionary<int, List<Point>>
                    {
                        [0] = new() { new Point(1659, 217), new Point(1663, 207), new Point(1667, 196), new Point(1668, 186), new Point(1670, 180), new Point(1671, 173) },
                        [1] = new() { new Point(1354, 535), new Point(1353, 521), new Point(1351, 512), new Point(1351, 505) },
                        [2] = new() { new Point(864, 946), new Point(869, 951) },
                    }
                }
            };

            var type = GestureClassifier.Classify(sample);

            Assert.AreEqual(RecordedGestureType.Trajectory, type);
        }

        [TestMethod]
        public void GetEffectiveGestureDurationMs_UsesActiveContactWindowInsteadOfWholeSession()
        {
            var session = new GestureSessionSnapshot
            {
                DurationMs = 448.347,
                ContactDownTimesMs = new Dictionary<int, double> { [0] = 386.26, [1] = 386.262, [2] = 401 },
                ContactUpTimesMs = new Dictionary<int, double> { [0] = 0.003, [1] = 440.173, [2] = 448.334 },
            };

            double durationMs = GestureSessionTiming.GetEffectiveGestureDurationMs(session);

            Assert.AreEqual(62.074, durationMs, 0.01);
        }

        [TestMethod]
        public void GetActivationCacheKey_SameProcessAndClass_DifferentWindowHandles_ProduceSameKey()
        {
            string first = SystemWindow.GetActivationCacheKey(1234, new System.IntPtr(0x10001), "Chrome_WidgetWin_1");
            string second = SystemWindow.GetActivationCacheKey(1234, new System.IntPtr(0x10002), "Chrome_WidgetWin_1");

            // 修复后缓存键不再包含 hWnd，相同进程+类名应产生相同 key
            Assert.AreEqual(first, second);
        }

        [TestMethod]
        public void GetCachedActivationMode_ExpiredEntry_ReturnsAuto()
        {
            SystemWindow.ResetActivationModeCache();
            string cacheKey = SystemWindow.GetActivationCacheKey(1234, new IntPtr(0x10001), "Chrome_WidgetWin_1");
            DateTime now = DateTime.UtcNow;

            SystemWindow.RememberSuccessfulActivationMode(cacheKey, WindowActivationMode.SafeMode, now.AddMinutes(-31));

            var mode = SystemWindow.GetCachedActivationMode(cacheKey, now);

            Assert.AreEqual(WindowActivationMode.Auto, mode);
            Assert.AreEqual(0, SystemWindow.GetActivationModeCacheCount());
        }

        [TestMethod]
        public void RememberSuccessfulActivationMode_TooManyEntries_PrunesOldestEntry()
        {
            SystemWindow.ResetActivationModeCache();
            DateTime now = DateTime.UtcNow;

            // 修复后缓存键不含 hWnd，用不同 processId 产生不同 key
            for (int index = 0; index <= 512; index++)
            {
                string cacheKey = SystemWindow.GetActivationCacheKey(index, new IntPtr(0x10000), "Chrome_WidgetWin_1");
                SystemWindow.RememberSuccessfulActivationMode(cacheKey, WindowActivationMode.SafeMode, now.AddSeconds(index));
            }

            string oldestKey = SystemWindow.GetActivationCacheKey(0, new IntPtr(0x10000), "Chrome_WidgetWin_1");

            Assert.AreEqual(512, SystemWindow.GetActivationModeCacheCount());
            Assert.AreEqual(WindowActivationMode.Auto, SystemWindow.GetCachedActivationMode(oldestKey, now.AddMinutes(1)));
        }
    }
}

