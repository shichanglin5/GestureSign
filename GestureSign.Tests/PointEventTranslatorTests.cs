using GestureSign.Common.Input;
using GestureSign.Common.Applications;
using GestureSign.Daemon.Input;
using GestureSign.Daemon.Triggers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Drawing;

namespace GestureSign.Tests
{
    [TestClass]
    public class PointEventTranslatorTests
    {
        [TestMethod]
        public void ShouldResetSourceDeviceAfterPointUp_AllReleased_ReturnsTrue()
        {
            var args = new InputPointsEventArgs(
                new List<InputPoint>
                {
                    new InputPoint(0, new Point(10, 10), DeviceStates.None),
                    new InputPoint(1, new Point(20, 20), DeviceStates.None),
                },
                Devices.TouchPad,
                2);

            bool shouldReset = PointEventTranslator.ShouldResetSourceDeviceAfterPointUp(args);

            Assert.IsTrue(shouldReset);
        }

        [TestMethod]
        public void ShouldResetSourceDeviceAfterPointUp_PartialRelease_ReturnsFalse()
        {
            var args = new InputPointsEventArgs(
                new List<InputPoint>
                {
                    new InputPoint(0, new Point(10, 10)),
                    new InputPoint(1, new Point(20, 20), DeviceStates.None),
                },
                Devices.TouchPad,
                2);

            bool shouldReset = PointEventTranslator.ShouldResetSourceDeviceAfterPointUp(args);

            Assert.IsFalse(shouldReset);
        }

        [TestMethod]
        public void ClassifyTouchPadEvent_WhenActiveContactCountIncreases_ReturnsDown()
        {
            var result = PointEventTranslator.ClassifyTouchPadEvent(previousActiveCount: 2, currentActiveCount: 3);

            Assert.AreEqual(TouchPadPointEventKind.Down, result);
        }

        [TestMethod]
        public void ClassifyTouchPadEvent_WhenActiveContactCountStaysSame_ReturnsMove()
        {
            var result = PointEventTranslator.ClassifyTouchPadEvent(previousActiveCount: 2, currentActiveCount: 2);

            Assert.AreEqual(TouchPadPointEventKind.Move, result);
        }

        [TestMethod]
        public void ClassifyTouchPadEvent_WhenActiveContactCountDrops_ReturnsUp()
        {
            var result = PointEventTranslator.ClassifyTouchPadEvent(previousActiveCount: 3, currentActiveCount: 2);

            Assert.AreEqual(TouchPadPointEventKind.Up, result);
        }

        [TestMethod]
        public void ShouldResetSessionBeforePointDown_WhenPreviousSessionEndedAndNewContactStarts_ReturnsTrue()
        {
            bool shouldReset = PointCapture.ShouldResetSessionBeforePointDown(
                hasTrackedSessionData: true,
                hasActiveContacts: false,
                state: CaptureState.Ready,
                points: new List<InputPoint>
                {
                    new InputPoint(0, new Point(10, 10)),
                });

            Assert.IsTrue(shouldReset);
        }

        [TestMethod]
        public void ShouldResetSessionBeforePointDown_WhenContactsStillActive_ReturnsFalse()
        {
            bool shouldReset = PointCapture.ShouldResetSessionBeforePointDown(
                hasTrackedSessionData: true,
                hasActiveContacts: true,
                state: CaptureState.Ready,
                points: new List<InputPoint>
                {
                    new InputPoint(0, new Point(10, 10)),
                });

            Assert.IsFalse(shouldReset);
        }

        [TestMethod]
        public void ShouldResetSessionBeforePointDown_IsSharedForRuntimeReadyState_ReturnsTrue()
        {
            bool shouldReset = PointCapture.ShouldResetSessionBeforePointDown(
                hasTrackedSessionData: true,
                hasActiveContacts: false,
                state: CaptureState.Ready,
                points: new List<InputPoint>
                {
                    new InputPoint(2, new Point(30, 30)),
                    new InputPoint(3, new Point(40, 40)),
                });

            Assert.IsTrue(shouldReset);
        }

        [TestMethod]
        public void ShouldHandleTwoFingerContinuous_WhenPeakIsThreeButActiveIsTwo_ReturnsTrue()
        {
            bool shouldHandle = ContinuousGestureTrigger.ShouldHandleTwoFingerContinuous(
                fingerCount: 3,
                activeFingerCount: 2);

            Assert.IsTrue(shouldHandle);
        }

        [TestMethod]
        public void ShouldHandleTwoFingerContinuous_WhenActiveIsNotTwo_ReturnsFalse()
        {
            bool shouldHandleOneFinger = ContinuousGestureTrigger.ShouldHandleTwoFingerContinuous(
                fingerCount: 2,
                activeFingerCount: 1);
            bool shouldHandleThreeFingers = ContinuousGestureTrigger.ShouldHandleTwoFingerContinuous(
                fingerCount: 3,
                activeFingerCount: 3);

            Assert.IsFalse(shouldHandleOneFinger);
            Assert.IsFalse(shouldHandleThreeFingers);
        }

        [TestMethod]
        public void ShouldHandleTwoFingerContinuous_WhenActiveMissing_UsesFingerCountFallback()
        {
            bool shouldHandle = ContinuousGestureTrigger.ShouldHandleTwoFingerContinuous(
                fingerCount: 2,
                activeFingerCount: 0);

            Assert.IsTrue(shouldHandle);
        }

        [TestMethod]
        public void GetInactivityTimeoutMs_WhenPeakFingerCountIsFour_UsesExtendedTimeout()
        {
            int timeoutMs = PointCapture.GetInactivityTimeoutMs(4);

            Assert.AreEqual(240, timeoutMs);
        }

        [TestMethod]
        public void GetInactivityTimeoutMs_WhenPeakFingerCountIsTwo_UsesBaseTimeout()
        {
            int timeoutMs = PointCapture.GetInactivityTimeoutMs(2);

            Assert.AreEqual(100, timeoutMs);
        }

    }
}
