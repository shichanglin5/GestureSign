using GestureSign.Common.Applications;
using GestureSign.Common.Input;
using GestureSign.Daemon.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;

namespace GestureSign.Tests
{
    [TestClass]
    public class ContactRecognizerTests
    {
        [TestMethod]
        public void MultiFingerTapRecognizer_TapLikeSession_Matches()
        {
            var recognizer = new MultiFingerTapRecognizer();
            var session = new GestureSessionSnapshot
            {
                FingerCount = 3,
                DurationMs = 120,
                ActiveContactIds = new List<int>(),
                AllPoints = new List<List<Point>>
                {
                    new() { new Point(10, 10), new Point(12, 11) },
                    new() { new Point(30, 10), new Point(31, 11) },
                    new() { new Point(50, 10), new Point(51, 11) },
                }
            };
            var analysis = new GestureAnalysis
            {
                FingerCount = 3,
                IsTapLike = true,
            };

            var result = recognizer.TryRecognize(session, analysis);

            Assert.IsTrue(result.IsMatch);
            Assert.AreEqual(ContactGestureKind.MultiFingerTap, result.Kind);
            Assert.AreEqual(3, result.FingerCount);
        }

        [TestMethod]
        public void MultiFingerClickRecognizer_PrimaryButtonSession_Matches()
        {
            var recognizer = new MultiFingerClickRecognizer();
            var session = new GestureSessionSnapshot
            {
                FingerCount = 2,
                DurationMs = 450,
                ActiveContactIds = new List<int>(),
                HasPrimaryButtonClick = true,
                PrimaryButtonFingerCount = 2,
                PrimaryButtonDownTimeMs = 120,
                PrimaryButtonUpTimeMs = 280,
                AllPoints = new List<List<Point>>
                {
                    new() { new Point(100, 100), new Point(101, 101) },
                    new() { new Point(140, 100), new Point(141, 101) },
                }
            };
            var analysis = GestureAnalyzer.Analyze(session.AllPoints, session.FingerCount, session.DurationMs);

            var result = recognizer.TryRecognize(session, analysis);

            Assert.IsTrue(result.IsMatch);
            Assert.AreEqual(ContactGestureKind.MultiFingerClick, result.Kind);
            Assert.AreEqual(2, result.FingerCount);
        }

        [TestMethod]
        public void TipTapRecognizer_LeftDirection_Matches()
        {
            var recognizer = new TipTapRecognizer();

            var session = new GestureSessionSnapshot
            {
                FingerCount = 2,
                DurationMs = 100,
                ActiveContactIds = new List<int> { 10 },
                ContactDownOrder = new List<int> { 10, 20 },
                ContactUpOrder = new List<int> { 20 },
                ContactDownTimesMs = new Dictionary<int, double> { [10] = 0, [20] = 80 },
                ContactUpTimesMs = new Dictionary<int, double> { [20] = 100 },
                ContactTrajectories = new Dictionary<int, List<Point>>
                {
                    [10] = new List<Point> { new Point(200, 200), new Point(202, 201) },
                    [20] = new List<Point> { new Point(120, 200), new Point(130, 201) },
                },
                AllPoints = new List<List<Point>>
                {
                    new() { new Point(200, 200), new Point(202, 201) },
                    new() { new Point(120, 200), new Point(130, 201) },
                }
            };
            var config = new TipTapGestureConfig
            {
                FingerCount = 2,
                FixFingerCount = 1,
                Direction = ContactGestureDirection.Left,
                Recognition = new TipTapRecognition
                {
                    DirectionDeadzonePx = 20,
                    RepeatCooldownMs = 0,
                }
            };

            var result = recognizer.ProcessSnapshot(session, config);

            Assert.IsTrue(result.IsMatch);
            Assert.AreEqual(ContactGestureKind.TipTap, result.Kind);
            Assert.AreEqual("fix1:left", result.Variant);
        }

        [TestMethod]
        public void TipTapRecognizer_LastDownFirstUpWithinTapWindow_ButFixHoldTooShort_DoesNotMatch()
        {
            var recognizer = new TipTapRecognizer();


            var session = new GestureSessionSnapshot
            {
                FingerCount = 2,
                DurationMs = 30,
                ContactDownOrder = new List<int> { 10, 20 },
                ContactUpOrder = new List<int> { 20 },
                ContactDownTimesMs = new Dictionary<int, double> { [10] = 0, [20] = 10 },
                ContactUpTimesMs = new Dictionary<int, double> { [20] = 30 },
                ContactTrajectories = new Dictionary<int, List<Point>>
                {
                    [10] = new List<Point> { new Point(200, 200), new Point(201, 200) },
                    [20] = new List<Point> { new Point(120, 200), new Point(126, 200) },
                }
            };

            var config = new TipTapGestureConfig
            {
                FingerCount = 2,
                FixFingerCount = 1,
                Direction = ContactGestureDirection.Left,
                Recognition = new TipTapRecognition
                {
                    FixMinHoldMs = 50,
                    DirectionDeadzonePx = 20,
                    RepeatCooldownMs = 0,
                }
            };

            var result = recognizer.ProcessSnapshot(session, config);

            Assert.IsFalse(result.IsMatch);
        }

        [TestMethod]
        public void TipTapRecognizer_TapDurationWithinSystemConfiguredWindow_Matches()
        {
            var recognizer = new TipTapRecognizer();


            var session = new GestureSessionSnapshot
            {
                FingerCount = 2,
                DurationMs = 756.194,
                ContactDownOrder = new List<int> { 0, 1 },
                ContactUpOrder = new List<int> { 1, 0 },
                ContactDownTimesMs = new Dictionary<int, double> { [0] = 0.003, [1] = 328.16 },
                ContactUpTimesMs = new Dictionary<int, double> { [1] = 477.934, [0] = 756.183 },
                ContactTrajectories = new Dictionary<int, List<Point>>
                {
                    [0] = new List<Point> { new Point(1439, 743) },
                    [1] = new List<Point> { new Point(1931, 743) },
                }
            };

            var config = new TipTapGestureConfig
            {
                FingerCount = 2,
                FixFingerCount = 1,
                Direction = ContactGestureDirection.Right,
                Recognition = new TipTapRecognition
                {
                    FixMinHoldMs = 50,
                    MaxTapDurationMs = 150,
                    FixStillThresholdPx = 12,
                    TapMaxMovementPx = 18,
                    DirectionDeadzonePx = 24,
                    RepeatCooldownMs = 0,
                }
            };

            var result = recognizer.ProcessSnapshot(session, config);

            Assert.IsTrue(result.IsMatch);
            Assert.AreEqual(ContactGestureKind.TipTap, result.Kind);
            Assert.AreEqual("fix1:right", result.Variant);
        }

        [TestMethod]
        public void MultiFingerTapRecognizer_FourFingerTapWithSingleOutlierJitter_StillMatches()
        {
            var recognizer = new MultiFingerTapRecognizer();
            var session = new GestureSessionSnapshot
            {
                FingerCount = 4,
                DurationMs = 141,
                ActiveContactIds = new List<int>(),
                AllPoints = new List<List<Point>>
                {
                    new() { new Point(2243, 391), new Point(2236, 405), new Point(2235, 411), new Point(2235, 405) },
                    new() { new Point(1768, 446), new Point(1767, 464) },
                    new() { new Point(843, 1129), new Point(836, 1127) },
                    new() { new Point(1359, 697), new Point(1359, 707) },
                }
            };
            var analysis = GestureAnalyzer.Analyze(session.AllPoints, session.FingerCount, session.DurationMs);

            var result = recognizer.TryRecognize(session, analysis);

            Assert.IsTrue(analysis.IsTapLike);
            Assert.IsTrue(result.IsMatch);
            Assert.AreEqual(ContactGestureKind.MultiFingerTap, result.Kind);
            Assert.AreEqual(4, result.FingerCount);
        }

        [TestMethod]
        public void MultiFingerTapRecognizer_FourFingerTap_DurationBetween300And360_MatchesWithExtraAllowance()
        {
            // 回归测试：4 指 Tap duration=320ms 超过基础 300ms，但在多指裕量 (300+60=360ms) 内应通过
            int oldTapMaxDurationMs = GestureSign.Common.Configuration.AppConfig.TapMaxDurationMs;
            try
            {
                GestureSign.Common.Configuration.AppConfig.TapMaxDurationMs = 300;
                var recognizer = new MultiFingerTapRecognizer();
                var session = new GestureSessionSnapshot
                {
                    FingerCount = 4,
                    DurationMs = 320,
                    ActiveContactIds = new List<int>(),
                    ContactDownTimesMs = new Dictionary<int, double> { { 0, 0 }, { 1, 5 }, { 2, 12 }, { 3, 18 } },
                    ContactUpTimesMs = new Dictionary<int, double> { { 0, 290 }, { 1, 300 }, { 2, 310 }, { 3, 320 } },
                    AllPoints = new List<List<Point>>
                    {
                        new() { new Point(10, 10), new Point(12, 11) },
                        new() { new Point(30, 10), new Point(31, 11) },
                        new() { new Point(50, 10), new Point(51, 11) },
                        new() { new Point(70, 10), new Point(71, 11) },
                    }
                };
                var analysis = GestureAnalyzer.Analyze(session.AllPoints, session.FingerCount, session.DurationMs);

                var result = recognizer.TryRecognize(session, analysis);

                Assert.IsTrue(analysis.IsTapLike, "IsTapLike 应通过（4指裕量 60ms）");
                Assert.IsTrue(result.IsMatch, "4 指 Tap duration=320ms 应在多指裕量范围内匹配");
                Assert.AreEqual(4, result.FingerCount);
            }
            finally
            {
                GestureSign.Common.Configuration.AppConfig.TapMaxDurationMs = oldTapMaxDurationMs;
            }
        }

        [TestMethod]
        public void MultiFingerTapRecognizer_FourFingerTap_DurationExceedsAllowance_DoesNotMatch()
        {
            // 4 指 Tap duration=370ms 超过 300+60=360ms 裕量上限，应失败
            int oldTapMaxDurationMs = GestureSign.Common.Configuration.AppConfig.TapMaxDurationMs;
            try
            {
                GestureSign.Common.Configuration.AppConfig.TapMaxDurationMs = 300;
                var recognizer = new MultiFingerTapRecognizer();
                var session = new GestureSessionSnapshot
                {
                    FingerCount = 4,
                    DurationMs = 370,
                    ActiveContactIds = new List<int>(),
                    ContactDownTimesMs = new Dictionary<int, double> { { 0, 0 }, { 1, 5 }, { 2, 12 }, { 3, 18 } },
                    ContactUpTimesMs = new Dictionary<int, double> { { 0, 340 }, { 1, 350 }, { 2, 360 }, { 3, 370 } },
                    AllPoints = new List<List<Point>>
                    {
                        new() { new Point(10, 10), new Point(12, 11) },
                        new() { new Point(30, 10), new Point(31, 11) },
                        new() { new Point(50, 10), new Point(51, 11) },
                        new() { new Point(70, 10), new Point(71, 11) },
                    }
                };
                var analysis = GestureAnalyzer.Analyze(session.AllPoints, session.FingerCount, session.DurationMs);

                var result = recognizer.TryRecognize(session, analysis);

                Assert.IsFalse(result.IsMatch, "4 指 Tap duration=370ms 超过裕量上限 360ms 应失败");
            }
            finally
            {
                GestureSign.Common.Configuration.AppConfig.TapMaxDurationMs = oldTapMaxDurationMs;
            }
        }

        [TestMethod]
        public void MultiFingerTapRecognizer_TwoFingerTap_DurationExceeds300_DoesNotMatch()
        {
            // 2 指没有额外裕量，duration=310ms 超过 300ms 应失败
            int oldTapMaxDurationMs = GestureSign.Common.Configuration.AppConfig.TapMaxDurationMs;
            try
            {
                GestureSign.Common.Configuration.AppConfig.TapMaxDurationMs = 300;
                var recognizer = new MultiFingerTapRecognizer();
                var session = new GestureSessionSnapshot
                {
                    FingerCount = 2,
                    DurationMs = 310,
                    ActiveContactIds = new List<int>(),
                    ContactDownTimesMs = new Dictionary<int, double> { { 0, 0 }, { 1, 5 } },
                    ContactUpTimesMs = new Dictionary<int, double> { { 0, 300 }, { 1, 310 } },
                    AllPoints = new List<List<Point>>
                    {
                        new() { new Point(10, 10), new Point(12, 11) },
                        new() { new Point(30, 10), new Point(31, 11) },
                    }
                };
                var analysis = GestureAnalyzer.Analyze(session.AllPoints, session.FingerCount, session.DurationMs);

                var result = recognizer.TryRecognize(session, analysis);

                Assert.IsFalse(result.IsMatch, "2 指 Tap 无额外裕量，310ms > 300ms 应失败");
            }
            finally
            {
                GestureSign.Common.Configuration.AppConfig.TapMaxDurationMs = oldTapMaxDurationMs;
            }
        }

        [TestMethod]
        public void MultiFingerTapRecognizer_WhenAnyContactStillActive_DoesNotMatch()
        {
            var recognizer = new MultiFingerTapRecognizer();
            var session = new GestureSessionSnapshot
            {
                FingerCount = 3,
                DurationMs = 120,
                ActiveContactIds = new List<int> { 1 },
                AllPoints = new List<List<Point>>
                {
                    new() { new Point(10, 10), new Point(12, 11) },
                    new() { new Point(30, 10), new Point(31, 11) },
                    new() { new Point(50, 10), new Point(51, 11) },
                }
            };
            var analysis = new GestureAnalysis
            {
                FingerCount = 3,
                IsTapLike = true,
            };

            var result = recognizer.TryRecognize(session, analysis);

            Assert.IsFalse(result.IsMatch);
        }

        [TestMethod]
        public void GestureAnalyzer_FourFingerTap_WithTightTapMaxDuration_UsesExtraAllowance()
        {
            int oldTapMaxDurationMs = GestureSign.Common.Configuration.AppConfig.TapMaxDurationMs;
            try
            {
                GestureSign.Common.Configuration.AppConfig.TapMaxDurationMs = 150;

                // 四指 tap，持续时间 200ms 超过 150ms 阈值，
                // 但 4 指有 (4-2)*30=60ms 额外裕量，有效阈值 210ms，应当通过
                var allPoints = new List<List<Point>>
                {
                    new() { new Point(1268, 777), new Point(1267, 788), new Point(1261, 787) },
                    new() { new Point(343, 1516), new Point(336, 1509), new Point(336, 1515) },
                    new() { new Point(1801, 737), new Point(1796, 747) },
                    new() { new Point(906, 1007), new Point(902, 1017) },
                };
                var analysis = GestureAnalyzer.Analyze(allPoints, 4, 200);

                Assert.IsTrue(analysis.IsTapLike, "四指 200ms tap 在 TapMaxDurationMs=150 时应通过（额外裕量 60ms）");
            }
            finally
            {
                GestureSign.Common.Configuration.AppConfig.TapMaxDurationMs = oldTapMaxDurationMs;
            }
        }

        [TestMethod]
        public void GestureAnalyzer_FourFingerTap_ExceedsAllowance_ReturnsFalse()
        {
            int oldTapMaxDurationMs = GestureSign.Common.Configuration.AppConfig.TapMaxDurationMs;
            try
            {
                GestureSign.Common.Configuration.AppConfig.TapMaxDurationMs = 150;

                // 四指 tap，持续时间 220ms 超过有效阈值 210ms (150+60)，应当失败
                var allPoints = new List<List<Point>>
                {
                    new() { new Point(100, 100), new Point(101, 101) },
                    new() { new Point(200, 100), new Point(201, 101) },
                    new() { new Point(300, 100), new Point(301, 101) },
                    new() { new Point(400, 100), new Point(401, 101) },
                };
                var analysis = GestureAnalyzer.Analyze(allPoints, 4, 220);

                Assert.IsFalse(analysis.IsTapLike, "四指 220ms tap 超过有效阈值 210ms (150+60) 应失败");
            }
            finally
            {
                GestureSign.Common.Configuration.AppConfig.TapMaxDurationMs = oldTapMaxDurationMs;
            }
        }

        [TestMethod]
        public void GestureAnalyzer_TwoFingerTap_NoExtraAllowance()
        {
            int oldTapMaxDurationMs = GestureSign.Common.Configuration.AppConfig.TapMaxDurationMs;
            try
            {
                GestureSign.Common.Configuration.AppConfig.TapMaxDurationMs = 150;

                // 二指 tap，持续时间 155ms 超过 150ms 阈值，
                // 2 指无额外裕量 (fingerCount <= 2)，应当失败
                var allPoints = new List<List<Point>>
                {
                    new() { new Point(100, 100), new Point(101, 101) },
                    new() { new Point(200, 100), new Point(201, 101) },
                };
                var analysis = GestureAnalyzer.Analyze(allPoints, 2, 155);

                Assert.IsFalse(analysis.IsTapLike, "二指 155ms tap 在 TapMaxDurationMs=150 时无额外裕量，应失败");
            }
            finally
            {
                GestureSign.Common.Configuration.AppConfig.TapMaxDurationMs = oldTapMaxDurationMs;
            }
        }

        [TestMethod]
        public void GestureAnalyzer_ThreeFingerTap_HasSmallExtraAllowance()
        {
            int oldTapMaxDurationMs = GestureSign.Common.Configuration.AppConfig.TapMaxDurationMs;
            try
            {
                GestureSign.Common.Configuration.AppConfig.TapMaxDurationMs = 150;

                // 三指 tap，持续时间 175ms，有效阈值 150 + (3-2)*30 = 180ms，应当通过
                var allPoints = new List<List<Point>>
                {
                    new() { new Point(100, 100), new Point(101, 101) },
                    new() { new Point(200, 100), new Point(201, 101) },
                    new() { new Point(300, 100), new Point(301, 101) },
                };
                var analysis = GestureAnalyzer.Analyze(allPoints, 3, 175);

                Assert.IsTrue(analysis.IsTapLike, "三指 175ms tap 在 TapMaxDurationMs=150 时应通过（额外裕量 30ms）");
            }
            finally
            {
                GestureSign.Common.Configuration.AppConfig.TapMaxDurationMs = oldTapMaxDurationMs;
            }
        }

        [TestMethod]
        public void GestureAnalyzer_ThreeFingerTap_MaxDistExactly18_UsesRelaxedPath()
        {
            // 3 指 tap，MaxPerFingerDistance 恰好 = 18（不满足 <18 严格检查），
            // 走宽松路径：多数手指 ≤ 20 且均值 < 18 且单指 ≤ 30 → 应通过
            var allPoints = new List<List<Point>>
            {
                new() { new Point(1470, 623), new Point(1470, 623), new Point(1470, 605) },  // dist=18
                new() { new Point(2079, 248) },  // dist=0
                new() { new Point(2479, 74) },   // dist=0
            };
            var analysis = GestureAnalyzer.Analyze(allPoints, 3, 140);

            Assert.IsTrue(analysis.IsTapLike, "三指 tap MaxDist=18 应走宽松路径通过");
        }

        [TestMethod]
        public void GestureAnalyzer_ThreeFingerTap_OneFingerSlightlyAbove18_StillPasses()
        {
            // 3 指 tap，一根手指 MaxDist≈25.9，但均值 14.2 < 18，
            // 其他两根手指 ≤ 20 → 宽松路径通过
            var allPoints = new List<List<Point>>
            {
                new() { new Point(2101, 249) },
                new() { new Point(1470, 623), new Point(1466, 613), new Point(1465, 607) },
                new() { new Point(2533, 68), new Point(2529, 73), new Point(2525, 82), new Point(2521, 91) },
            };
            var analysis = GestureAnalyzer.Analyze(allPoints, 3, 156);

            Assert.IsTrue(analysis.IsTapLike, "三指 tap 一根手指 25.9px 在宽松路径下应通过");
        }

        [TestMethod]
        public void GestureAnalyzer_TwoFingerTap_MaxDistAbove18_FailsWithoutRelaxedPath()
        {
            // 2 指 tap 没有宽松路径，MaxDist > 18 应失败
            var allPoints = new List<List<Point>>
            {
                new() { new Point(100, 100), new Point(100, 120) },  // dist=20
                new() { new Point(200, 100) },
            };
            var analysis = GestureAnalyzer.Analyze(allPoints, 2, 120);

            Assert.IsFalse(analysis.IsTapLike, "二指 tap MaxDist=20 无宽松路径应失败");
        }

        [TestMethod]
        public void ContactGestureResult_None_HasNoPublicSetters()
        {
            var properties = typeof(ContactGestureResult).GetProperties(BindingFlags.Instance | BindingFlags.Public);

            Assert.IsTrue(properties.All(property => property.SetMethod == null || !property.SetMethod.IsPublic));
            Assert.IsFalse(ContactGestureResult.None.IsMatch);
            Assert.AreEqual(ContactGestureKind.None, ContactGestureResult.None.Kind);
        }

        [TestMethod]
        public void MultiFingerClickRecognizer_SmallPressDisplacement_Matches()
        {
            var session = new GestureSessionSnapshot
            {
                FingerCount = 3,
                DurationMs = 500,
                HasPrimaryButtonClick = true,
                PrimaryButtonFingerCount = 3,
                PrimaryButtonDownTimeMs = 100,
                PrimaryButtonUpTimeMs = 300,
            };
            // 按压期间位移 5px，远小于默认阈值 18px
            Assert.IsTrue(MultiFingerClickRecognizer.IsMatch(session, 5.0));
        }

        [TestMethod]
        public void MultiFingerClickRecognizer_LargePressDisplacement_Rejects()
        {
            var session = new GestureSessionSnapshot
            {
                FingerCount = 3,
                DurationMs = 500,
                HasPrimaryButtonClick = true,
                PrimaryButtonFingerCount = 3,
                PrimaryButtonDownTimeMs = 100,
                PrimaryButtonUpTimeMs = 300,
            };
            // 按压期间位移 100px，超过默认阈值
            Assert.IsFalse(MultiFingerClickRecognizer.IsMatch(session, 100.0));
        }

        [TestMethod]
        public void MultiFingerClickRecognizer_LargeSessionTrajectoryButSmallPressDisplacement_Matches()
        {
            // 模拟连续 Click 场景：session 轨迹很长（手指在 rebase 后移动了很多），
            // 但按钮按下期间位移很小
            var session = new GestureSessionSnapshot
            {
                FingerCount = 3,
                DurationMs = 2000,
                HasPrimaryButtonClick = true,
                PrimaryButtonFingerCount = 3,
                PrimaryButtonDownTimeMs = 1800,
                PrimaryButtonUpTimeMs = 1900,
                AllPoints = new List<List<Point>>
                {
                    // 轨迹从 (0,0) 到 (500,500)，但按压期间手指没动
                    new() { new Point(0, 0), new Point(100, 100), new Point(300, 300), new Point(500, 500) },
                    new() { new Point(10, 0), new Point(110, 100), new Point(310, 300), new Point(510, 500) },
                    new() { new Point(20, 0), new Point(120, 100), new Point(320, 300), new Point(520, 500) },
                }
            };
            // 按压期间位移仅 3px
            double pressDisplacement = 3.0;
            Assert.IsTrue(MultiFingerClickRecognizer.IsMatch(session, pressDisplacement));

            // 而如果用 session 轨迹的 MaxPerFingerDistance（约 707px），则不会匹配
            var analysis = GestureAnalyzer.Analyze(session.AllPoints, 3, 2000);
            Assert.IsTrue(analysis.MaxPerFingerDistance > 100, $"Session trajectory distance should be large: {analysis.MaxPerFingerDistance}");
        }

        [TestMethod]
        public void MultiFingerClickRecognizer_TryRecognize_UsesAnalysisAsFallback()
        {
            var recognizer = new MultiFingerClickRecognizer();
            var session = new GestureSessionSnapshot
            {
                FingerCount = 2,
                DurationMs = 200,
                HasPrimaryButtonClick = true,
                PrimaryButtonFingerCount = 2,
                PrimaryButtonDownTimeMs = 50,
                PrimaryButtonUpTimeMs = 150,
                AllPoints = new List<List<Point>>
                {
                    new() { new Point(100, 100), new Point(102, 101) },
                    new() { new Point(200, 100), new Point(201, 101) },
                }
            };
            var analysis = GestureAnalyzer.Analyze(session.AllPoints, 2, 200);

            // TryRecognize（终端分类）使用 analysis.MaxPerFingerDistance 作为 fallback
            var result = recognizer.TryRecognize(session, analysis);
            Assert.IsTrue(result.IsMatch);
            Assert.AreEqual(ContactGestureKind.MultiFingerClick, result.Kind);
        }

        [TestMethod]
        public void MultiFingerClickRecognizer_PressDurationExceedsThreshold_Rejects()
        {
            var session = new GestureSessionSnapshot
            {
                FingerCount = 3,
                DurationMs = 1000,
                HasPrimaryButtonClick = true,
                PrimaryButtonFingerCount = 3,
                PrimaryButtonDownTimeMs = 100,
                PrimaryButtonUpTimeMs = 800, // 按压 700ms，超过默认 600ms
            };
            Assert.IsFalse(MultiFingerClickRecognizer.IsMatch(session, 5.0));
        }

        [TestMethod]
        public void MultiFingerClickRecognizer_PressDurationWithinThreshold_Matches()
        {
            var session = new GestureSessionSnapshot
            {
                FingerCount = 3,
                DurationMs = 1000,
                HasPrimaryButtonClick = true,
                PrimaryButtonFingerCount = 3,
                PrimaryButtonDownTimeMs = 100,
                PrimaryButtonUpTimeMs = 650, // 按压 550ms，在默认 600ms 以内
            };
            Assert.IsTrue(MultiFingerClickRecognizer.IsMatch(session, 5.0));
        }
    }
}
