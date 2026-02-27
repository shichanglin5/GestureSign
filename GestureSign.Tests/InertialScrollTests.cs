using Microsoft.VisualStudio.TestTools.UnitTesting;
using GestureSign.Common.Applications;

namespace GestureSign.Tests
{
    [TestClass]
    public class InertialScrollTests
    {
        [TestMethod]
        public void VelocityVector_Constructor_CalculatesCorrectMagnitude()
        {
            var velocity = new VelocityVector(300, 400);
            Assert.AreEqual(300, velocity.VelocityX, 0.1);
            Assert.AreEqual(400, velocity.VelocityY, 0.1);
            Assert.AreEqual(500, velocity.Magnitude, 0.1);
        }

        [TestMethod]
        public void VelocityVector_IsSignificant_ReturnsTrueForHighVelocity()
        {
            var velocity = new VelocityVector(100, 0);
            Assert.IsTrue(velocity.IsSignificant(50));
            Assert.IsFalse(velocity.IsSignificant(150));
        }

        [TestMethod]
        public void InertialScrollSettings_DefaultValues_AreCorrect()
        {
            var settings = new InertialScrollSettings();
            Assert.AreEqual(ScrollDirection.Both, settings.Direction);
            Assert.AreEqual(150.0, settings.PixelsPerScrollUnit, 0.01);
            Assert.AreEqual(1.5, settings.AccelerationFactor, 0.01);
            Assert.IsFalse(settings.ReverseDirection);
            Assert.IsFalse(settings.ReverseHorizontalDirection);
            Assert.AreEqual(0.25, settings.NoiseRatio, 0.01);
        }
    }
}
