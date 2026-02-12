using Microsoft.VisualStudio.TestTools.UnitTesting;
using GestureSign.Common.Applications;
using GestureSign.CorePlugins.InertialScroll;
using System;

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
            var settings = new GestureSign.CorePlugins.InertialScroll.InertialScrollSettings();
            Assert.AreEqual(GestureSign.CorePlugins.InertialScroll.ScrollDirection.Vertical, settings.Direction);
            Assert.AreEqual(30.0, settings.PixelsPerScrollUnit, 0.01);
            Assert.AreEqual(1.0, settings.AccelerationFactor, 0.01);
            Assert.IsFalse(settings.ReverseDirection);
            Assert.IsFalse(settings.ReverseHorizontalDirection);
        }

        [TestMethod]
        public void InertialScrollPlugin_Name_IsNotNull()
        {
            var plugin = new InertialScrollPlugin();
            // Note: Localization may not be initialized in test environment
            // Just verify the property doesn't throw an exception
            var name = plugin.Name;
            Assert.IsNotNull(name);
        }

        [TestMethod]
        public void InertialScrollPlugin_IsAction_ReturnsTrue()
        {
            var plugin = new InertialScrollPlugin();
            Assert.IsTrue(plugin.IsAction);
        }
    }
}
