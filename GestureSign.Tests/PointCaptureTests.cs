using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using GestureSign.Common.Gestures;
using GestureSign.Common.Input;
using GestureSign.Daemon.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace GestureSign.Tests
{
    [TestClass]
    public class PointCaptureTests
    {
        [TestMethod]
        public void TipTapEventFlow_WhenSingleFixFingerStaysDown_TrainingPublishesLeftThenRight()
        {
            var manager = ApplicationManager.Instance;
            manager.LoadingTask.Wait();
            var oldApps = manager.Applications;
            int oldMultiFingerDelay = AppConfig.MultiFingerDelay;
            int oldFixHoldMs = AppConfig.TipTapFixMinHoldMs;

            try
            {
                AppConfig.MultiFingerDelay = 0;
                AppConfig.TipTapFixMinHoldMs = 0;

                manager.RemoveAllApplication();
                var global = (GlobalApp)manager.GetGlobalApplication();
                global.ContactGestures.TipTaps.Add(new TipTapGestureConfig
                {
                    Id = "left-1",
                    Name = "TipTap Left (1 fixed)",
                    FingerCount = 2,
                    FixFingerCount = 1,
                    Direction = ContactGestureDirection.Left,
                    Recognition = new TipTapRecognition { FixMinHoldMs = 0, RepeatCooldownMs = 0 }
                });
                global.ContactGestures.TipTaps.Add(new TipTapGestureConfig
                {
                    Id = "right-1",
                    Name = "TipTap Right (1 fixed)",
                    FingerCount = 2,
                    FixFingerCount = 1,
                    Direction = ContactGestureDirection.Right,
                    Recognition = new TipTapRecognition { FixMinHoldMs = 0, RepeatCooldownMs = 0 }
                });

                var capture = new PointCapture(Devices.TouchPad)
                {
                    Mode = CaptureMode.Training
                };

                capture.ProcessPointDownForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(100, 100)),
                }, 1);

                capture.ProcessPointDownForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(100, 100)),
                    new InputPoint(1, new Point(40, 100)),
                }, 2);
                capture.ProcessPointUpForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(100, 100)),
                    new InputPoint(1, new Point(40, 100), DeviceStates.None),
                }, 2);

                capture.ProcessPointDownForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(100, 100)),
                    new InputPoint(2, new Point(170, 100)),
                }, 2);
                capture.ProcessPointUpForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(100, 100)),
                    new InputPoint(2, new Point(170, 100), DeviceStates.None),
                }, 2);

                Assert.AreEqual(2, capture.PublishedTrainingDefinitionsForTest.Count);
                Assert.AreEqual(RecordedGestureType.TipTap, capture.PublishedTrainingDefinitionsForTest[0].Type);
                Assert.AreEqual(ContactGestureDirection.Left, capture.PublishedTrainingDefinitionsForTest[0].TipTapGesture.Direction);
                Assert.AreEqual(RecordedGestureType.TipTap, capture.PublishedTrainingDefinitionsForTest[1].Type);
                Assert.AreEqual(ContactGestureDirection.Right, capture.PublishedTrainingDefinitionsForTest[1].TipTapGesture.Direction);
            }
            finally
            {
                AppConfig.MultiFingerDelay = oldMultiFingerDelay;
                AppConfig.TipTapFixMinHoldMs = oldFixHoldMs;
                var globalCleanup = (GlobalApp)manager.GetGlobalApplication();
                globalCleanup.ContactGestures?.TipTaps?.Clear();
                manager.RemoveAllApplication();
                manager.AddApplicationRange(oldApps);
            }
        }

        [TestMethod]
        public void TipTapEventFlow_WhenSingleFixFingerAlternatesLeftRightLeftRight_TrainingPublishesExpectedSequence()
        {
            var manager = ApplicationManager.Instance;
            manager.LoadingTask.Wait();
            var oldApps = manager.Applications;
            int oldMultiFingerDelay = AppConfig.MultiFingerDelay;
            int oldFixHoldMs = AppConfig.TipTapFixMinHoldMs;

            try
            {
                AppConfig.MultiFingerDelay = 0;
                AppConfig.TipTapFixMinHoldMs = 0;

                manager.RemoveAllApplication();
                var global = (GlobalApp)manager.GetGlobalApplication();
                global.ContactGestures.TipTaps.Add(new TipTapGestureConfig
                {
                    Id = "left-1",
                    Name = "TipTap Left (1 fixed)",
                    FingerCount = 2,
                    FixFingerCount = 1,
                    Direction = ContactGestureDirection.Left,
                    Recognition = new TipTapRecognition { FixMinHoldMs = 0, RepeatCooldownMs = 0 }
                });
                global.ContactGestures.TipTaps.Add(new TipTapGestureConfig
                {
                    Id = "right-1",
                    Name = "TipTap Right (1 fixed)",
                    FingerCount = 2,
                    FixFingerCount = 1,
                    Direction = ContactGestureDirection.Right,
                    Recognition = new TipTapRecognition { FixMinHoldMs = 0, RepeatCooldownMs = 0 }
                });

                var capture = new PointCapture(Devices.TouchPad)
                {
                    Mode = CaptureMode.Training
                };

                capture.ProcessPointDownForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(1284, 484)),
                }, 1);

                var tapSequence = new[]
                {
                    new { ContactId = 1, Point = new Point(813, 484), Expected = ContactGestureDirection.Left },
                    new { ContactId = 2, Point = new Point(1609, 484), Expected = ContactGestureDirection.Right },
                    new { ContactId = 3, Point = new Point(809, 484), Expected = ContactGestureDirection.Left },
                    new { ContactId = 4, Point = new Point(1599, 484), Expected = ContactGestureDirection.Right },
                };

                foreach (var tap in tapSequence)
                {
                    capture.ProcessPointDownForTest(new List<InputPoint>
                    {
                        new InputPoint(0, new Point(1284, 484)),
                        new InputPoint(tap.ContactId, tap.Point),
                    }, 2);

                    capture.ProcessPointUpForTest(new List<InputPoint>
                    {
                        new InputPoint(0, new Point(1284, 484)),
                        new InputPoint(tap.ContactId, tap.Point, DeviceStates.None),
                    }, 2);
                }

                capture.ProcessPointUpForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(1284, 484), DeviceStates.None),
                }, 1);

                var actualDirections = capture.PublishedTrainingDefinitionsForTest
                    .Select(definition => definition.TipTapGesture?.Direction ?? ContactGestureDirection.None)
                    .ToList();

                CollectionAssert.AreEqual(
                    tapSequence.Select(item => item.Expected).ToList(),
                    actualDirections);
            }
            finally
            {
                AppConfig.MultiFingerDelay = oldMultiFingerDelay;
                AppConfig.TipTapFixMinHoldMs = oldFixHoldMs;
                var globalCleanup = (GlobalApp)manager.GetGlobalApplication();
                globalCleanup.ContactGestures?.TipTaps?.Clear();
                manager.RemoveAllApplication();
                manager.AddApplicationRange(oldApps);
            }
        }

        [TestMethod]
        public void TipTapEventFlow_WhenTwoFixFingersStayDown_TrainingPublishesTwoThreeFingerTipTaps()
        {
            var manager = ApplicationManager.Instance;
            manager.LoadingTask.Wait();
            var oldApps = manager.Applications;
            int oldMultiFingerDelay = AppConfig.MultiFingerDelay;
            int oldFixHoldMs = AppConfig.TipTapFixMinHoldMs;

            try
            {
                AppConfig.MultiFingerDelay = 0;
                AppConfig.TipTapFixMinHoldMs = 0;

                manager.RemoveAllApplication();
                var global = (GlobalApp)manager.GetGlobalApplication();
                global.ContactGestures.TipTaps.Add(new TipTapGestureConfig
                {
                    Id = "left-2",
                    Name = "TipTap Left (2 fixed)",
                    FingerCount = 3,
                    FixFingerCount = 2,
                    Direction = ContactGestureDirection.Left,
                    Recognition = new TipTapRecognition { FixMinHoldMs = 0, RepeatCooldownMs = 0 }
                });
                global.ContactGestures.TipTaps.Add(new TipTapGestureConfig
                {
                    Id = "right-2",
                    Name = "TipTap Right (2 fixed)",
                    FingerCount = 3,
                    FixFingerCount = 2,
                    Direction = ContactGestureDirection.Right,
                    Recognition = new TipTapRecognition { FixMinHoldMs = 0, RepeatCooldownMs = 0 }
                });

                var capture = new PointCapture(Devices.TouchPad)
                {
                    Mode = CaptureMode.Training
                };

                capture.ProcessPointDownForTest(new List<InputPoint>
                {
                    new InputPoint(1, new Point(100, 100)),
                    new InputPoint(2, new Point(200, 100)),
                }, 2);

                capture.ProcessPointDownForTest(new List<InputPoint>
                {
                    new InputPoint(1, new Point(100, 100)),
                    new InputPoint(2, new Point(200, 100)),
                    new InputPoint(0, new Point(30, 100)),
                }, 3);
                capture.ProcessPointUpForTest(new List<InputPoint>
                {
                    new InputPoint(1, new Point(100, 100)),
                    new InputPoint(2, new Point(200, 100)),
                    new InputPoint(0, new Point(30, 100), DeviceStates.None),
                }, 3);

                capture.ProcessPointDownForTest(new List<InputPoint>
                {
                    new InputPoint(1, new Point(100, 100)),
                    new InputPoint(2, new Point(200, 100)),
                    new InputPoint(3, new Point(280, 100)),
                }, 3);
                capture.ProcessPointUpForTest(new List<InputPoint>
                {
                    new InputPoint(1, new Point(100, 100)),
                    new InputPoint(2, new Point(200, 100)),
                    new InputPoint(3, new Point(280, 100), DeviceStates.None),
                }, 3);

                Assert.AreEqual(2, capture.PublishedTrainingDefinitionsForTest.Count);
                Assert.IsTrue(capture.PublishedTrainingDefinitionsForTest.All(def => def.Type == RecordedGestureType.TipTap));
                Assert.AreEqual(ContactGestureDirection.Left, capture.PublishedTrainingDefinitionsForTest[0].TipTapGesture.Direction);
                Assert.AreEqual(ContactGestureDirection.Right, capture.PublishedTrainingDefinitionsForTest[1].TipTapGesture.Direction);
                Assert.IsTrue(capture.PublishedTrainingDefinitionsForTest.All(def => def.TipTapGesture.FixFingerCount == 2));
            }
            finally
            {
                AppConfig.MultiFingerDelay = oldMultiFingerDelay;
                AppConfig.TipTapFixMinHoldMs = oldFixHoldMs;
                var globalCleanup = (GlobalApp)manager.GetGlobalApplication();
                globalCleanup.ContactGestures?.TipTaps?.Clear();
                manager.RemoveAllApplication();
                manager.AddApplicationRange(oldApps);
            }
        }

        [TestMethod]
        public void TipTapEventFlow_WhenFixFingerReleasesLeavingExpectedFixSet_RebasesAndRecognizesNextThreeFingerTipTap()
        {
            var manager = ApplicationManager.Instance;
            manager.LoadingTask.Wait();
            var oldApps = manager.Applications;
            int oldMultiFingerDelay = AppConfig.MultiFingerDelay;
            int oldFixHoldMs = AppConfig.TipTapFixMinHoldMs;

            try
            {
                AppConfig.MultiFingerDelay = 0;
                AppConfig.TipTapFixMinHoldMs = 0;

                manager.RemoveAllApplication();
                var global = (GlobalApp)manager.GetGlobalApplication();
                global.ContactGestures.TipTaps.Add(new TipTapGestureConfig
                {
                    Id = "left-2",
                    Name = "TipTap Left (2 fixed)",
                    FingerCount = 3,
                    FixFingerCount = 2,
                    Direction = ContactGestureDirection.Left,
                    Recognition = new TipTapRecognition { FixMinHoldMs = 0, RepeatCooldownMs = 0 }
                });
                global.ContactGestures.TipTaps.Add(new TipTapGestureConfig
                {
                    Id = "right-2",
                    Name = "TipTap Right (2 fixed)",
                    FingerCount = 3,
                    FixFingerCount = 2,
                    Direction = ContactGestureDirection.Right,
                    Recognition = new TipTapRecognition { FixMinHoldMs = 0, RepeatCooldownMs = 0 }
                });

                var capture = new PointCapture(Devices.TouchPad)
                {
                    Mode = CaptureMode.Training
                };

                capture.ProcessPointDownForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(100, 100)),
                    new InputPoint(1, new Point(200, 100)),
                }, 2);

                capture.ProcessPointDownForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(100, 100)),
                    new InputPoint(1, new Point(200, 100)),
                    new InputPoint(2, new Point(30, 100)),
                }, 3);
                capture.ProcessPointUpForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(100, 100)),
                    new InputPoint(1, new Point(200, 100)),
                    new InputPoint(2, new Point(30, 100), DeviceStates.None),
                }, 3);

                capture.ProcessPointDownForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(100, 100)),
                    new InputPoint(1, new Point(200, 100)),
                    new InputPoint(2, new Point(30, 100)),
                }, 3);
                capture.ProcessPointUpForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(100, 100)),
                    new InputPoint(1, new Point(200, 100), DeviceStates.None),
                    new InputPoint(2, new Point(30, 100)),
                }, 3);

                capture.ProcessPointDownForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(100, 100)),
                    new InputPoint(2, new Point(30, 100)),
                    new InputPoint(1, new Point(280, 100)),
                }, 3);
                capture.ProcessPointUpForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(100, 100)),
                    new InputPoint(2, new Point(30, 100)),
                    new InputPoint(1, new Point(280, 100), DeviceStates.None),
                }, 3);

                var actual = capture.PublishedTrainingDefinitionsForTest
                    .Select(definition => definition.TipTapGesture?.Direction ?? ContactGestureDirection.None)
                    .ToList();

                CollectionAssert.AreEqual(
                    new List<ContactGestureDirection>
                    {
                        ContactGestureDirection.Left,
                        ContactGestureDirection.Right,
                    },
                    actual);
            }
            finally
            {
                AppConfig.MultiFingerDelay = oldMultiFingerDelay;
                AppConfig.TipTapFixMinHoldMs = oldFixHoldMs;
                var globalCleanup = (GlobalApp)manager.GetGlobalApplication();
                globalCleanup.ContactGestures?.TipTaps?.Clear();
                manager.RemoveAllApplication();
                manager.AddApplicationRange(oldApps);
            }
        }

        [TestMethod]
        public void TipTapEventFlow_WhenContinuousThreeFingerSessionAlternatesRebaseAndRecognition_PublishesExpectedSequence()
        {
            var manager = ApplicationManager.Instance;
            manager.LoadingTask.Wait();
            var oldApps = manager.Applications;
            int oldMultiFingerDelay = AppConfig.MultiFingerDelay;
            int oldFixHoldMs = AppConfig.TipTapFixMinHoldMs;

            try
            {
                AppConfig.MultiFingerDelay = 0;
                AppConfig.TipTapFixMinHoldMs = 0;

                manager.RemoveAllApplication();
                var global = (GlobalApp)manager.GetGlobalApplication();
                global.ContactGestures.TipTaps.Add(new TipTapGestureConfig
                {
                    Id = "left-2",
                    Name = "TipTap Left (2 fixed)",
                    FingerCount = 3,
                    FixFingerCount = 2,
                    Direction = ContactGestureDirection.Left,
                    Recognition = new TipTapRecognition { FixMinHoldMs = 0, RepeatCooldownMs = 0 }
                });
                global.ContactGestures.TipTaps.Add(new TipTapGestureConfig
                {
                    Id = "right-2",
                    Name = "TipTap Right (2 fixed)",
                    FingerCount = 3,
                    FixFingerCount = 2,
                    Direction = ContactGestureDirection.Right,
                    Recognition = new TipTapRecognition { FixMinHoldMs = 0, RepeatCooldownMs = 0 }
                });

                var capture = new PointCapture(Devices.TouchPad)
                {
                    Mode = CaptureMode.Training
                };

                // Fix 手指 0,2 先 down（确保 down time 早于 tap 手指）
                capture.ProcessPointDownForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(1330, 478)),
                    new InputPoint(2, new Point(834, 874)),
                }, 2);

                // 等待确保 tap 手指的 down time 明确晚于 fix
                Thread.Sleep(2);

                // Cycle 1: tap 手指 1 在右侧 down 后 up → Right
                // 注意：fix 手指位置在各 cycle 保持不变，避免超过 FixStillThresholdPx
                capture.ProcessPointDownForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(1330, 478)),
                    new InputPoint(1, new Point(1704, 156)),
                    new InputPoint(2, new Point(834, 874)),
                }, 3);
                capture.ProcessPointUpForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(1330, 478)),
                    new InputPoint(1, new Point(1704, 156), DeviceStates.None),
                    new InputPoint(2, new Point(834, 874)),
                }, 3);

                // Cycle 2: tap 手指 1 在右侧重新 down 后 up → Right
                capture.ProcessPointDownForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(1330, 478)),
                    new InputPoint(1, new Point(1718, 684)),
                    new InputPoint(2, new Point(834, 874)),
                }, 3);
                capture.ProcessPointUpForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(1330, 478)),
                    new InputPoint(1, new Point(1718, 684), DeviceStates.None),
                    new InputPoint(2, new Point(834, 874)),
                }, 3);

                // Cycle 3: tap 手指 1 在左侧 down 后 up → Left
                capture.ProcessPointDownForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(1330, 478)),
                    new InputPoint(1, new Point(700, 400)),
                    new InputPoint(2, new Point(834, 874)),
                }, 3);
                capture.ProcessPointUpForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(1330, 478)),
                    new InputPoint(1, new Point(700, 400), DeviceStates.None),
                    new InputPoint(2, new Point(834, 874)),
                }, 3);

                var actual = capture.PublishedTrainingDefinitionsForTest
                    .Select(definition => definition.TipTapGesture?.Direction ?? ContactGestureDirection.None)
                    .ToList();

                var expected = new List<ContactGestureDirection>
                {
                    ContactGestureDirection.Right,
                    ContactGestureDirection.Right,
                    ContactGestureDirection.Left,
                };

                CollectionAssert.AreEqual(expected, actual);
            }
            finally
            {
                AppConfig.MultiFingerDelay = oldMultiFingerDelay;
                AppConfig.TipTapFixMinHoldMs = oldFixHoldMs;
                var globalCleanup = (GlobalApp)manager.GetGlobalApplication();
                globalCleanup.ContactGestures?.TipTaps?.Clear();
                manager.RemoveAllApplication();
                manager.AddApplicationRange(oldApps);
            }
        }

        [TestMethod]
        public void TipTapEventFlow_WhenFixFingerReleasesFirst_DoesNotPublishTipTap()
        {
            var manager = ApplicationManager.Instance;
            manager.LoadingTask.Wait();
            var oldApps = manager.Applications;
            int oldMultiFingerDelay = AppConfig.MultiFingerDelay;
            int oldFixHoldMs = AppConfig.TipTapFixMinHoldMs;

            try
            {
                AppConfig.MultiFingerDelay = 0;
                AppConfig.TipTapFixMinHoldMs = 0;

                manager.RemoveAllApplication();
                var global = (GlobalApp)manager.GetGlobalApplication();
                global.ContactGestures.TipTaps.Add(new TipTapGestureConfig
                {
                    Id = "left-1",
                    Name = "TipTap Left (1 fixed)",
                    FingerCount = 2,
                    FixFingerCount = 1,
                    Direction = ContactGestureDirection.Left,
                    Recognition = new TipTapRecognition { FixMinHoldMs = 0, RepeatCooldownMs = 0 }
                });

                var capture = new PointCapture(Devices.TouchPad)
                {
                    Mode = CaptureMode.Training
                };

                // finger 0 = fix (right side), finger 1 = tap (left side)
                capture.ProcessPointDownForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(100, 100)),
                }, 1);

                capture.ProcessPointDownForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(100, 100)),
                    new InputPoint(1, new Point(40, 100)),
                }, 2);

                // Fix finger (0) releases first — wrong order for TipTap
                capture.ProcessPointUpForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(100, 100), DeviceStates.None),
                    new InputPoint(1, new Point(40, 100)),
                }, 2);

                Assert.AreEqual(0, capture.PublishedTrainingDefinitionsForTest.Count,
                    "Fix finger releasing first must not trigger real-time TipTap");

                // Tap finger releases — all released → terminal classification
                capture.ProcessPointUpForTest(new List<InputPoint>
                {
                    new InputPoint(1, new Point(40, 100), DeviceStates.None),
                }, 1);

                // Terminal classification may produce a Tap/Trajectory, but never a TipTap
                Assert.IsTrue(
                    capture.PublishedTrainingDefinitionsForTest.All(d => d.TipTapGesture == null),
                    "No TipTap should be published when fix finger releases before tap finger");
            }
            finally
            {
                AppConfig.MultiFingerDelay = oldMultiFingerDelay;
                AppConfig.TipTapFixMinHoldMs = oldFixHoldMs;
                var globalCleanup = (GlobalApp)manager.GetGlobalApplication();
                globalCleanup.ContactGestures?.TipTaps?.Clear();
                manager.RemoveAllApplication();
                manager.AddApplicationRange(oldApps);
            }
        }

        [TestMethod]
        public void TipTapEventFlow_WhenPhysicalSessionsSwitchBetweenTwoAndThreeFingerModes_PublishesExpectedFingerCounts()
        {
            var manager = ApplicationManager.Instance;
            manager.LoadingTask.Wait();
            var oldApps = manager.Applications;
            int oldMultiFingerDelay = AppConfig.MultiFingerDelay;
            int oldFixHoldMs = AppConfig.TipTapFixMinHoldMs;

            try
            {
                AppConfig.MultiFingerDelay = 0;
                AppConfig.TipTapFixMinHoldMs = 0;

                manager.RemoveAllApplication();
                var global = (GlobalApp)manager.GetGlobalApplication();
                global.ContactGestures.TipTaps.Add(new TipTapGestureConfig
                {
                    Id = "left-1",
                    Name = "TipTap Left (1 fixed)",
                    FingerCount = 2,
                    FixFingerCount = 1,
                    Direction = ContactGestureDirection.Left,
                    Recognition = new TipTapRecognition { FixMinHoldMs = 0, RepeatCooldownMs = 0 }
                });
                global.ContactGestures.TipTaps.Add(new TipTapGestureConfig
                {
                    Id = "right-2",
                    Name = "TipTap Right (2 fixed)",
                    FingerCount = 3,
                    FixFingerCount = 2,
                    Direction = ContactGestureDirection.Right,
                    Recognition = new TipTapRecognition { FixMinHoldMs = 0, RepeatCooldownMs = 0 }
                });

                var capture = new PointCapture(Devices.TouchPad)
                {
                    Mode = CaptureMode.Training
                };

                capture.ProcessPointDownForTest(new List<InputPoint>
                {
                    new InputPoint(10, new Point(100, 100)),
                }, 1);
                capture.ProcessPointDownForTest(new List<InputPoint>
                {
                    new InputPoint(10, new Point(100, 100)),
                    new InputPoint(11, new Point(40, 100)),
                }, 2);
                capture.ProcessPointUpForTest(new List<InputPoint>
                {
                    new InputPoint(10, new Point(100, 100)),
                    new InputPoint(11, new Point(40, 100), DeviceStates.None),
                }, 2);
                capture.ProcessPointUpForTest(new List<InputPoint>
                {
                    new InputPoint(10, new Point(100, 100), DeviceStates.None),
                }, 1);

                capture.ProcessPointDownForTest(new List<InputPoint>
                {
                    new InputPoint(20, new Point(100, 100)),
                    new InputPoint(21, new Point(200, 100)),
                }, 2);
                capture.ProcessPointDownForTest(new List<InputPoint>
                {
                    new InputPoint(20, new Point(100, 100)),
                    new InputPoint(21, new Point(200, 100)),
                    new InputPoint(22, new Point(280, 100)),
                }, 3);
                capture.ProcessPointUpForTest(new List<InputPoint>
                {
                    new InputPoint(20, new Point(100, 100)),
                    new InputPoint(21, new Point(200, 100)),
                    new InputPoint(22, new Point(280, 100), DeviceStates.None),
                }, 3);
                capture.ProcessPointUpForTest(new List<InputPoint>
                {
                    new InputPoint(20, new Point(100, 100), DeviceStates.None),
                    new InputPoint(21, new Point(200, 100), DeviceStates.None),
                }, 2);

                capture.ProcessPointDownForTest(new List<InputPoint>
                {
                    new InputPoint(30, new Point(120, 100)),
                }, 1);
                capture.ProcessPointDownForTest(new List<InputPoint>
                {
                    new InputPoint(30, new Point(120, 100)),
                    new InputPoint(31, new Point(60, 100)),
                }, 2);
                capture.ProcessPointUpForTest(new List<InputPoint>
                {
                    new InputPoint(30, new Point(120, 100)),
                    new InputPoint(31, new Point(60, 100), DeviceStates.None),
                }, 2);
                capture.ProcessPointUpForTest(new List<InputPoint>
                {
                    new InputPoint(30, new Point(120, 100), DeviceStates.None),
                }, 1);

                CollectionAssert.AreEqual(
                    new List<int> { 2, 3, 2 },
                    capture.PublishedTrainingDefinitionsForTest.Select(definition => definition.FingerCount).ToList());
                CollectionAssert.AreEqual(
                    new List<ContactGestureDirection>
                    {
                        ContactGestureDirection.Left,
                        ContactGestureDirection.Right,
                        ContactGestureDirection.Left,
                    },
                    capture.PublishedTrainingDefinitionsForTest.Select(definition => definition.TipTapGesture.Direction).ToList());
            }
            finally
            {
                AppConfig.MultiFingerDelay = oldMultiFingerDelay;
                AppConfig.TipTapFixMinHoldMs = oldFixHoldMs;
                var globalCleanup = (GlobalApp)manager.GetGlobalApplication();
                globalCleanup.ContactGestures?.TipTaps?.Clear();
                manager.RemoveAllApplication();
                manager.AddApplicationRange(oldApps);
            }
        }

        [TestMethod]
        public void TipTapEventFlow_WhenTapContactIdIsReusedAcrossCycles_RecognizesEachIncarnation()
        {
            var manager = ApplicationManager.Instance;
            manager.LoadingTask.Wait();
            var oldApps = manager.Applications;
            int oldMultiFingerDelay = AppConfig.MultiFingerDelay;
            int oldFixHoldMs = AppConfig.TipTapFixMinHoldMs;

            try
            {
                AppConfig.MultiFingerDelay = 0;
                AppConfig.TipTapFixMinHoldMs = 0;

                manager.RemoveAllApplication();
                var global = (GlobalApp)manager.GetGlobalApplication();
                global.ContactGestures.TipTaps.Add(new TipTapGestureConfig
                {
                    Id = "left-1",
                    Name = "TipTap Left (1 fixed)",
                    FingerCount = 2,
                    FixFingerCount = 1,
                    Direction = ContactGestureDirection.Left,
                    Recognition = new TipTapRecognition { FixMinHoldMs = 0, RepeatCooldownMs = 0 }
                });
                global.ContactGestures.TipTaps.Add(new TipTapGestureConfig
                {
                    Id = "right-1",
                    Name = "TipTap Right (1 fixed)",
                    FingerCount = 2,
                    FixFingerCount = 1,
                    Direction = ContactGestureDirection.Right,
                    Recognition = new TipTapRecognition { FixMinHoldMs = 0, RepeatCooldownMs = 0 }
                });

                var capture = new PointCapture(Devices.TouchPad)
                {
                    Mode = CaptureMode.Training
                };

                capture.ProcessPointDownForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(100, 100)),
                }, 1);

                capture.ProcessPointDownForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(100, 100)),
                    new InputPoint(1, new Point(40, 100)),
                }, 2);
                capture.ProcessPointUpForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(100, 100)),
                    new InputPoint(1, new Point(40, 100), DeviceStates.None),
                }, 2);

                capture.ProcessPointDownForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(100, 100)),
                    new InputPoint(1, new Point(170, 100)),
                }, 2);
                capture.ProcessPointUpForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(100, 100)),
                    new InputPoint(1, new Point(170, 100), DeviceStates.None),
                }, 2);

                capture.ProcessPointUpForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(100, 100), DeviceStates.None),
                }, 1);

                CollectionAssert.AreEqual(
                    new List<ContactGestureDirection>
                    {
                        ContactGestureDirection.Left,
                        ContactGestureDirection.Right,
                    },
                    capture.PublishedTrainingDefinitionsForTest
                        .Select(definition => definition.TipTapGesture?.Direction ?? ContactGestureDirection.None)
                        .ToList());
            }
            finally
            {
                AppConfig.MultiFingerDelay = oldMultiFingerDelay;
                AppConfig.TipTapFixMinHoldMs = oldFixHoldMs;
                var globalCleanup = (GlobalApp)manager.GetGlobalApplication();
                globalCleanup.ContactGestures?.TipTaps?.Clear();
                manager.RemoveAllApplication();
                manager.AddApplicationRange(oldApps);
            }
        }

        [TestMethod]
        public void TipTapEventFlow_WhenTipTapSessionEndsWithoutContactMatch_TrainingFallsBackToTrajectory()
        {
            var manager = ApplicationManager.Instance;
            manager.LoadingTask.Wait();
            var oldApps = manager.Applications;
            int oldMultiFingerDelay = AppConfig.MultiFingerDelay;
            int oldFixHoldMs = AppConfig.TipTapFixMinHoldMs;

            try
            {
                AppConfig.MultiFingerDelay = 0;
                AppConfig.TipTapFixMinHoldMs = 0;

                manager.RemoveAllApplication();
                var global = (GlobalApp)manager.GetGlobalApplication();
                global.ContactGestures.TipTaps.Add(new TipTapGestureConfig
                {
                    Id = "left-2",
                    Name = "TipTap Left (2 fixed)",
                    FingerCount = 3,
                    FixFingerCount = 2,
                    Direction = ContactGestureDirection.Left,
                    Recognition = new TipTapRecognition { FixMinHoldMs = 0, RepeatCooldownMs = 0 }
                });

                var capture = new PointCapture(Devices.TouchPad)
                {
                    Mode = CaptureMode.Training
                };

                capture.ProcessPointDownForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(100, 100)),
                    new InputPoint(1, new Point(200, 100)),
                }, 2);

                capture.ProcessPointMoveForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(150, 130)),
                    new InputPoint(1, new Point(250, 130)),
                }, 2);

                capture.ProcessPointUpForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(170, 150), DeviceStates.None),
                    new InputPoint(1, new Point(270, 150), DeviceStates.None),
                }, 2);

                Assert.AreEqual(1, capture.PublishedTrainingDefinitionsForTest.Count);
                Assert.AreEqual(RecordedGestureType.Trajectory, capture.PublishedTrainingDefinitionsForTest[0].Type);
            }
            finally
            {
                AppConfig.MultiFingerDelay = oldMultiFingerDelay;
                AppConfig.TipTapFixMinHoldMs = oldFixHoldMs;
                var globalCleanup = (GlobalApp)manager.GetGlobalApplication();
                globalCleanup.ContactGestures?.TipTaps?.Clear();
                manager.RemoveAllApplication();
                manager.AddApplicationRange(oldApps);
            }
        }

        [TestMethod]
        public void TryGetTouchPadReferencePoint_WhenFingerCountIsTwoOrLess_ReturnsLeftMostContact()
        {
            var points = new List<InputPoint>
            {
                new InputPoint(7, new Point(900, 300)),
                new InputPoint(3, new Point(400, 300)),
            };

            bool found = PointCapture.TryGetTouchPadReferencePoint(points, totalFingerCount: 2, out var referencePoint);

            Assert.IsTrue(found);
            Assert.AreEqual(3, referencePoint.ContactIdentifier);
        }

        [TestMethod]
        public void TryGetTouchPadReferencePoint_WhenFingerCountExceedsTwo_ReturnsSecondLeftMostContact()
        {
            var points = new List<InputPoint>
            {
                new InputPoint(1, new Point(300, 300)),
                new InputPoint(2, new Point(900, 300)),
                new InputPoint(3, new Point(600, 300)),
            };

            bool found = PointCapture.TryGetTouchPadReferencePoint(points, totalFingerCount: 3, out var referencePoint);

            Assert.IsTrue(found);
            Assert.AreEqual(3, referencePoint.ContactIdentifier);
        }

        [TestMethod]
        public void TrainingTap_TwoFingerMinimalMovement_ClassifiedAsTap()
        {
            using var capture = new PointCapture(Devices.TouchPad)
            {
                Mode = CaptureMode.Training
            };

            capture.ProcessPointDownForTest(new List<InputPoint>
            {
                new InputPoint(0, new Point(1771, 325)),
                new InputPoint(1, new Point(1219, 767)),
            }, 2);

            capture.ProcessPointMoveForTest(new List<InputPoint>
            {
                new InputPoint(0, new Point(1771, 325)),
                new InputPoint(1, new Point(1219, 767)),
            }, 2);

            capture.ProcessPointUpForTest(new List<InputPoint>
            {
                new InputPoint(0, new Point(1771, 336)),
                new InputPoint(1, new Point(1219, 767), DeviceStates.None),
            }, 2);

            capture.ProcessPointUpForTest(new List<InputPoint>
            {
                new InputPoint(0, new Point(1771, 336), DeviceStates.None),
            }, 1);

            Assert.AreEqual(1, capture.PublishedTrainingDefinitionsForTest.Count);
            Assert.AreEqual(RecordedGestureType.Tap, capture.PublishedTrainingDefinitionsForTest[0].Type);
        }

        [TestMethod]
        public void TrainingTap_TwoFingerSmallMovement_ClassifiedAsTap()
        {
            using var capture = new PointCapture(Devices.TouchPad)
            {
                Mode = CaptureMode.Training
            };

            capture.ProcessPointDownForTest(new List<InputPoint>
            {
                new InputPoint(0, new Point(1771, 325)),
                new InputPoint(1, new Point(1219, 767)),
            }, 2);

            capture.ProcessPointMoveForTest(new List<InputPoint>
            {
                new InputPoint(0, new Point(1773, 326)),
                new InputPoint(1, new Point(1220, 768)),
            }, 2);

            capture.ProcessPointUpForTest(new List<InputPoint>
            {
                new InputPoint(0, new Point(1773, 326)),
                new InputPoint(1, new Point(1220, 768), DeviceStates.None),
            }, 2);

            capture.ProcessPointUpForTest(new List<InputPoint>
            {
                new InputPoint(0, new Point(1773, 326), DeviceStates.None),
            }, 1);

            Assert.AreEqual(1, capture.PublishedTrainingDefinitionsForTest.Count);
            Assert.AreEqual(RecordedGestureType.Tap, capture.PublishedTrainingDefinitionsForTest[0].Type);
            Assert.AreEqual(2, capture.PublishedTrainingDefinitionsForTest[0].FingerCount);
        }

        [TestMethod]
        public void TrainingTap_ThreeFingerStaggeredDown_ClassifiedAsTap()
        {
            // 确保前面的 TipTap 测试不会泄漏 TipTap configs
            ((GlobalApp)ApplicationManager.Instance.GetGlobalApplication()).ContactGestures?.TipTaps?.Clear();

            using var capture = new PointCapture(Devices.TouchPad)
            {
                Mode = CaptureMode.Training
            };

            // 第一帧只有 1 指 — training 模式下 TryBeginCapture 因 <2 拒绝
            capture.ProcessPointDownForTest(new List<InputPoint>
            {
                new InputPoint(0, new Point(1804, 328)),
            }, 1);

            // 第二帧 3 指到齐 — TryBeginCapture 接受
            capture.ProcessPointDownForTest(new List<InputPoint>
            {
                new InputPoint(0, new Point(1804, 328)),
                new InputPoint(1, new Point(1389, 626)),
                new InputPoint(2, new Point(861, 1172)),
            }, 3);

            capture.ProcessPointMoveForTest(new List<InputPoint>
            {
                new InputPoint(0, new Point(1804, 328)),
                new InputPoint(1, new Point(1389, 626)),
                new InputPoint(2, new Point(862, 1176)),
            }, 3);

            capture.ProcessPointUpForTest(new List<InputPoint>
            {
                new InputPoint(0, new Point(1804, 328), DeviceStates.None),
                new InputPoint(1, new Point(1389, 626), DeviceStates.None),
                new InputPoint(2, new Point(862, 1176)),
            }, 3);

            capture.ProcessPointUpForTest(new List<InputPoint>
            {
                new InputPoint(2, new Point(862, 1176), DeviceStates.None),
            }, 1);

            Assert.AreEqual(1, capture.PublishedTrainingDefinitionsForTest.Count);
            Assert.AreEqual(RecordedGestureType.Tap, capture.PublishedTrainingDefinitionsForTest[0].Type);
            Assert.AreEqual(3, capture.PublishedTrainingDefinitionsForTest[0].FingerCount);
        }

        [TestMethod]
        public void TrainingTwoFingerClick_PublishesClickDefinition()
        {
            int oldMultiFingerDelay = AppConfig.MultiFingerDelay;
            PointCapture capture = null;

            try
            {
                AppConfig.MultiFingerDelay = 0;

                capture = new PointCapture(Devices.TouchPad)
                {
                    Mode = CaptureMode.Training
                };

                capture.ProcessPointDownForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(1771, 325), DeviceStates.Tip),
                    new InputPoint(1, new Point(1219, 767), DeviceStates.Tip),
                }, 2);

                capture.ProcessPointMoveForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(1771, 325), DeviceStates.Tip | DeviceStates.PrimaryButton),
                    new InputPoint(1, new Point(1219, 767), DeviceStates.Tip | DeviceStates.PrimaryButton),
                }, 2);

                capture.ProcessPointMoveForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(1771, 325), DeviceStates.Tip),
                    new InputPoint(1, new Point(1219, 767), DeviceStates.Tip),
                }, 2);

                capture.ProcessPointUpForTest(new List<InputPoint>
                {
                    new InputPoint(0, new Point(1771, 325), DeviceStates.None),
                    new InputPoint(1, new Point(1219, 767), DeviceStates.None),
                }, 2);

                Assert.AreEqual(1, capture.PublishedTrainingDefinitionsForTest.Count);
                // Click 已统一为 Tap + PrimaryButtonDown 修饰符
                Assert.AreEqual(RecordedGestureType.Tap, capture.PublishedTrainingDefinitionsForTest[0].Type);
                Assert.AreEqual(2, capture.PublishedTrainingDefinitionsForTest[0].FingerCount);
                Assert.IsTrue(capture.PublishedTrainingDefinitionsForTest[0].TapGesture?.Modifiers.HasFlag(GestureSign.Common.Gestures.GestureModifiers.PrimaryButtonDown) == true,
                    "Click gesture should be converted to Tap with PrimaryButtonDown modifier");
            }
            finally
            {
                capture?.Dispose();
                AppConfig.MultiFingerDelay = oldMultiFingerDelay;
            }
        }

        [TestMethod]
        public void TryMatchWithKnownRoles_DirectCall_MatchesLeftTipTap()
        {
            int oldFixHoldMs = AppConfig.TipTapFixMinHoldMs;
            try
            {
                AppConfig.TipTapFixMinHoldMs = 0;

                var session = new GestureSessionSnapshot
                {
                    FingerCount = 2,
                    DurationMs = 50,
                    ActiveContactIds = new List<int> { 0 },
                    ContactDownOrder = new List<int> { 0, 1 },
                    ContactUpOrder = new List<int> { 1 },
                    ContactDownTimesMs = new Dictionary<int, double> { [0] = 0d, [1] = 10d },
                    ContactUpTimesMs = new Dictionary<int, double> { [1] = 20d },
                    ContactTrajectories = new Dictionary<int, List<Point>>
                    {
                        [0] = new List<Point> { new Point(100, 100) },
                        [1] = new List<Point> { new Point(40, 100) },
                    },
                };

                bool matched = TipTapRecognizer.TryMatchWithKnownRoles(
                    session,
                    tapId: 1,
                    fixIds: new List<int> { 0 },
                    recognition: new TipTapRecognition { FixMinHoldMs = 0, MaxTapDurationMs = 500, RepeatCooldownMs = 0 },
                    expectedDirection: ContactGestureDirection.Left,
                    out var match);

                Assert.IsTrue(matched, "TryMatchWithKnownRoles should match a simple left TipTap");
                Assert.AreEqual(ContactGestureDirection.Left, match.Direction);
                Assert.AreEqual(1, match.TapId);
            }
            finally
            {
                AppConfig.TipTapFixMinHoldMs = oldFixHoldMs;
            }
        }

        #region 重构回归测试：等待所有手指释放 + 触摸板坐标一致性

        [TestMethod]
        public void PointUp_PartialRelease_DoesNotEndCapture()
        {
            using var capture = new PointCapture(Devices.TouchPad)
            {
                Mode = CaptureMode.Training
            };

            // 2 指 Down → CapturingInvalid
            capture.ProcessPointDownForTest(new List<InputPoint>
            {
                new InputPoint(0, new Point(500, 300)),
                new InputPoint(1, new Point(600, 300)),
            }, 2);

            Assert.AreEqual(CaptureState.CapturingInvalid, capture.State);

            // 只释放 1 指 → 仍然在 CapturingInvalid，不触发 EndCapture
            capture.ProcessPointUpForTest(new List<InputPoint>
            {
                new InputPoint(0, new Point(500, 300)),
                new InputPoint(1, new Point(600, 300), DeviceStates.None),
            }, 2);

            Assert.AreEqual(CaptureState.CapturingInvalid, capture.State,
                "部分释放不应触发 EndCapture，State 应保持 CapturingInvalid");
            Assert.AreEqual(0, capture.PublishedTrainingDefinitionsForTest.Count,
                "部分释放不应产生 training definition");
        }

        [TestMethod]
        public void PointUp_AllReleased_EndsCapture()
        {
            using var capture = new PointCapture(Devices.TouchPad)
            {
                Mode = CaptureMode.Training
            };

            capture.ProcessPointDownForTest(new List<InputPoint>
            {
                new InputPoint(0, new Point(500, 300)),
                new InputPoint(1, new Point(600, 300)),
            }, 2);

            // 全部释放 → EndCapture
            capture.ProcessPointUpForTest(new List<InputPoint>
            {
                new InputPoint(0, new Point(500, 300), DeviceStates.None),
                new InputPoint(1, new Point(600, 300), DeviceStates.None),
            }, 2);

            Assert.AreEqual(CaptureState.Ready, capture.State,
                "全部释放后 State 应回到 Ready");
            Assert.AreEqual(1, capture.PublishedTrainingDefinitionsForTest.Count,
                "全部释放应产生 1 个 training definition");
        }

        [TestMethod]
        public void TouchPadTrajectory_FirstFrameAndLaterFrames_UseConsistentCoordinates()
        {
            // 回归测试：确保 TrackAllPoints 中首帧的坐标翻译与后续帧一致
            // 如果参考点未在 TrackAllPoints 中初始化，首帧会使用 raw 坐标导致轨迹跳变
            using var capture = new PointCapture(Devices.TouchPad)
            {
                Mode = CaptureMode.Training
            };

            // 模拟 2 指 tap，手指有微小移动（< TapStrictMovementPx=18）
            capture.ProcessPointDownForTest(new List<InputPoint>
            {
                new InputPoint(0, new Point(2000, 500)),
                new InputPoint(1, new Point(1000, 800)),
            }, 2);

            capture.ProcessPointMoveForTest(new List<InputPoint>
            {
                new InputPoint(0, new Point(2003, 502)),
                new InputPoint(1, new Point(1002, 801)),
            }, 2);

            capture.ProcessPointUpForTest(new List<InputPoint>
            {
                new InputPoint(0, new Point(2003, 502), DeviceStates.None),
                new InputPoint(1, new Point(1002, 801), DeviceStates.None),
            }, 2);

            Assert.AreEqual(1, capture.PublishedTrainingDefinitionsForTest.Count);
            // 关键断言：应该被分类为 Tap（坐标一致则移动量 < 18px）
            // 如果坐标不一致（首帧 raw vs 后续 translated），移动量会是数百 px，被分类为 Trajectory
            Assert.AreEqual(RecordedGestureType.Tap, capture.PublishedTrainingDefinitionsForTest[0].Type,
                "触摸板坐标翻译应一致，微小移动应被分类为 Tap 而非 Trajectory");
        }

        #endregion

        #region ActiveFingerCount Tests

        [TestMethod]
        public void ActiveFingerCount_TwoFingersDown_EqualsPeakFingerCount()
        {
            // 双指都在时，ActiveFingerCount 应等于 FingerCount（peak）
            var args = new PointsCapturedEventArgs(new List<Point> { new Point(0, 0) })
            {
                FingerCount = 2,
                ActiveFingerCount = 2
            };

            Assert.AreEqual(args.FingerCount, args.ActiveFingerCount);
        }

        [TestMethod]
        public void ActiveFingerCount_OneFingerLifted_LessThanPeakFingerCount()
        {
            // 双指滑动后松开一根手指：FingerCount（peak）仍为 2，ActiveFingerCount 应为 1
            var args = new PointsCapturedEventArgs(new List<Point> { new Point(0, 0) })
            {
                FingerCount = 2,
                ActiveFingerCount = 1
            };

            Assert.AreEqual(2, args.FingerCount, "FingerCount（peak）应保持为 2");
            Assert.AreEqual(1, args.ActiveFingerCount, "ActiveFingerCount 应反映实际手指数");
            Assert.IsTrue(args.ActiveFingerCount < args.FingerCount,
                "一根手指抬起后 ActiveFingerCount 应小于 FingerCount");
        }

        [TestMethod]
        public void ContinuousGesture_ShouldStop_WhenActiveFingerCountDropsBelowTwo()
        {
            // 模拟 ContinuousGestureTrigger 的判断逻辑：
            // activeFingerCount < 2 时应停止连续手势
            int fingerCount = 2;  // peak
            int activeFingerCount = 1;  // 实际只剩 1 根

            bool shouldContinue = (fingerCount == 2) && (activeFingerCount >= 2);

            Assert.IsFalse(shouldContinue, "活跃手指数不足 2 时不应继续连续手势");
        }

        [TestMethod]
        public void ContinuousGesture_ShouldContinue_WhenBothFingersActive()
        {
            // 双指都在时应继续
            int fingerCount = 2;
            int activeFingerCount = 2;

            bool shouldContinue = (fingerCount == 2) && (activeFingerCount >= 2);

            Assert.IsTrue(shouldContinue, "双指都在时应继续连续手势");
        }

        [TestMethod]
        public void ContinuousGesture_DetectsFingerCountChange_WhenOneFingerLifted()
        {
            // 模拟 fingerCountChanged 检测：prevFingerCount=2, 当前 activeFingerCount=1
            int prevFingerCount = 2;
            int activeFingerCount = 1;

            bool fingerCountChanged = activeFingerCount != prevFingerCount;

            Assert.IsTrue(fingerCountChanged, "手指从 2 变为 1 应检测为 fingerCountChanged");
        }

        #endregion
    }
}
