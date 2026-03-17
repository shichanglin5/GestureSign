using GestureSign.Common.Applications;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace GestureSign.Tests
{
    [TestClass]
    public class TwoFingerGestureSettingsTests
    {
        [TestMethod]
        public void ApplicationBase_HasTwoFingerGestures_ByDefault()
        {
            var app = new UserApp();

            Assert.IsNotNull(app.TwoFingerGestures);
            Assert.AreEqual(InheritSwitch.Inherit, app.TwoFingerGestures.Scroll);
            Assert.AreEqual(InheritSwitch.Inherit, app.TwoFingerGestures.Zoom);
        }

        [TestMethod]
        public void Serialize_TwoFingerGestures_RoundTrips()
        {
            var app = new UserApp();
            app.TwoFingerGestures.Scroll = InheritSwitch.Enabled;
            app.TwoFingerGestures.Zoom = InheritSwitch.Disabled;
            app.TwoFingerGestures.ZoomSettings.ZoomSensitivity = 1.8;

            var json = JsonConvert.SerializeObject(app);
            var clone = JsonConvert.DeserializeObject<UserApp>(json);

            Assert.IsNotNull(clone);
            Assert.IsNotNull(clone.TwoFingerGestures);
            Assert.AreEqual(InheritSwitch.Enabled, clone.TwoFingerGestures.Scroll);
            Assert.AreEqual(InheritSwitch.Disabled, clone.TwoFingerGestures.Zoom);
            Assert.AreEqual(1.8, clone.TwoFingerGestures.ZoomSettings.ZoomSensitivity, 0.001);
        }

        [TestMethod]
        public void TwoFingerGestureSettings_Default_DoesNotSerialize()
        {
            var settings = new TwoFingerGestureSettings();

            Assert.IsFalse(settings.ShouldSerialize());
        }
    }
}
