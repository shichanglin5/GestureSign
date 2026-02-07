using Microsoft.VisualStudio.TestTools.UnitTesting;
using GestureSign.Common.Applications;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace GestureSign.Tests
{
    [TestClass]
    public class ActionIsEnabledTests
    {
        #region Data Model Tests

        [TestMethod]
        public void Action_IsEnabled_DefaultsToTrue()
        {
            var action = new Action();
            Assert.IsTrue(action.IsEnabled);
        }

        [TestMethod]
        public void Action_IsEnabled_SetToFalse_FiresPropertyChanged()
        {
            var action = new Action();
            string changedProperty = null;
            ((INotifyPropertyChanged)action).PropertyChanged += (s, e) => changedProperty = e.PropertyName;

            action.IsEnabled = false;

            Assert.IsFalse(action.IsEnabled);
            Assert.AreEqual(nameof(Action.IsEnabled), changedProperty);
        }

        [TestMethod]
        public void Action_IsEnabled_SetToSameValue_DoesNotFirePropertyChanged()
        {
            var action = new Action();
            bool fired = false;
            ((INotifyPropertyChanged)action).PropertyChanged += (s, e) => fired = true;

            action.IsEnabled = true; // same as default

            Assert.IsFalse(fired);
        }

        [TestMethod]
        public void Action_DeepCopy_PreservesIsEnabled()
        {
            var action = new Action { Name = "Test", GestureName = "Swipe", IsEnabled = false };
            action.AddCommand(new Command { PluginClass = "TestPlugin", IsEnabled = true });

            var copy = action.DeepCopy();

            Assert.IsFalse(copy.IsEnabled);
        }

        [TestMethod]
        public void Action_DeepCopy_PreservesIsEnabledTrue()
        {
            var action = new Action { Name = "Test", GestureName = "Swipe", IsEnabled = true };
            action.AddCommand(new Command { PluginClass = "TestPlugin", IsEnabled = true });

            var copy = action.DeepCopy();

            Assert.IsTrue(copy.IsEnabled);
        }

        #endregion

        #region Serialization Tests

        [TestMethod]
        public void Serialize_ActionWithIsEnabledFalse_ContainsField()
        {
            var action = new Action { Name = "Test", GestureName = "Swipe", IsEnabled = false };
            action.AddCommand(new Command { PluginClass = "TestPlugin", IsEnabled = true });

            var json = JsonConvert.SerializeObject(action);

            Assert.IsTrue(json.Contains("\"IsEnabled\":false") || json.Contains("\"IsEnabled\": false"),
                "Serialized JSON should contain IsEnabled:false");
        }

        [TestMethod]
        public void Deserialize_OldJsonWithoutIsEnabled_DefaultsToTrue()
        {
            // Simulate old JSON that doesn't have IsEnabled field
            var json = @"{""Name"":""Test"",""GestureName"":""Swipe"",""Commands"":[{""PluginClass"":""TestPlugin"",""IsEnabled"":true}]}";

            var action = JsonConvert.DeserializeObject<Action>(json,
                new JsonSerializerSettings
                {
                    Converters = new List<JsonConverter> { new CommandConverter() }
                });

            Assert.IsNotNull(action);
            Assert.IsTrue(action.IsEnabled, "Old JSON without IsEnabled should default to true for backward compatibility");
        }

        [TestMethod]
        public void Deserialize_NewJsonWithIsEnabledFalse_ReadsCorrectly()
        {
            var json = @"{""IsEnabled"":false,""Name"":""Test"",""GestureName"":""Swipe"",""Commands"":[{""PluginClass"":""TestPlugin"",""IsEnabled"":true}]}";

            var action = JsonConvert.DeserializeObject<Action>(json,
                new JsonSerializerSettings
                {
                    Converters = new List<JsonConverter> { new CommandConverter() }
                });

            Assert.IsNotNull(action);
            Assert.IsFalse(action.IsEnabled);
        }

        [TestMethod]
        public void Serialize_Roundtrip_PreservesIsEnabled()
        {
            var original = new Action { Name = "Test", GestureName = "Swipe", IsEnabled = false };
            original.AddCommand(new Command { PluginClass = "TestPlugin", IsEnabled = true });

            var settings = new JsonSerializerSettings
            {
                Converters = new List<JsonConverter> { new CommandConverter() }
            };

            var json = JsonConvert.SerializeObject(original, settings);
            var deserialized = JsonConvert.DeserializeObject<Action>(json, settings);

            Assert.IsNotNull(deserialized);
            Assert.AreEqual(original.IsEnabled, deserialized.IsEnabled);
            Assert.AreEqual(original.Name, deserialized.Name);
        }

        #endregion

        #region Business Logic Tests - GetDefinedAction

        private static IApplication CreateUserApp(string name, params IAction[] actions)
        {
            var app = new UserApp { Name = name };
            foreach (var action in actions)
                app.AddAction(action);
            return app;
        }

        private static Action CreateAction(string name, string gestureName, bool isEnabled = true, bool commandEnabled = true)
        {
            var action = new Action { Name = name, GestureName = gestureName, IsEnabled = isEnabled };
            action.AddCommand(new Command { PluginClass = "TestPlugin", IsEnabled = commandEnabled });
            return action;
        }

        [TestMethod]
        public void GetDefinedAction_EnabledAction_ReturnsAction()
        {
            var action = CreateAction("SwipeUp", "gesture1", isEnabled: true);
            var app = CreateUserApp("TestApp", action);

            var result = ApplicationManager.Instance.GetDefinedAction("gesture1", new[] { app }, false);

            Assert.AreEqual(1, result.Count());
            Assert.AreEqual("SwipeUp", result.First().Name);
        }

        [TestMethod]
        public void GetDefinedAction_DisabledAction_FiltersOut()
        {
            var action = CreateAction("SwipeUp", "gesture1", isEnabled: false);
            var app = CreateUserApp("TestApp", action);

            var result = ApplicationManager.Instance.GetDefinedAction("gesture1", new[] { app }, false);

            Assert.AreEqual(0, result.Count(), "Disabled action should be filtered out");
        }

        [TestMethod]
        public void GetDefinedAction_MixedEnabledActions_ReturnsOnlyEnabled()
        {
            var enabledAction = CreateAction("SwipeUp", "gesture1", isEnabled: true);
            var disabledAction = CreateAction("SwipeDown", "gesture1", isEnabled: false);
            var app = CreateUserApp("TestApp", enabledAction, disabledAction);

            var result = ApplicationManager.Instance.GetDefinedAction("gesture1", new[] { app }, false);

            Assert.AreEqual(1, result.Count());
            Assert.AreEqual("SwipeUp", result.First().Name);
        }

        [TestMethod]
        public void GetDefinedAction_DisabledAction_FallsBackToGlobal()
        {
            // Setup: add a global action to ApplicationManager
            var manager = ApplicationManager.Instance;
            // Wait for loading to complete
            manager.LoadingTask.Wait();

            var globalApp = manager.GetGlobalApplication();
            var globalAction = CreateAction("GlobalSwipe", "gesture1", isEnabled: true);
            globalApp.AddAction(globalAction);

            try
            {
                // User app has disabled action for same gesture
                var disabledAction = CreateAction("AppSwipe", "gesture1", isEnabled: false);
                var userApp = CreateUserApp("TestApp", disabledAction);

                var result = manager.GetDefinedAction("gesture1", new[] { userApp }, true);

                Assert.AreEqual(1, result.Count(), "Should fall back to global action when user app action is disabled");
                Assert.AreEqual("GlobalSwipe", result.First().Name);
            }
            finally
            {
                // Cleanup: remove the global action we added
                globalApp.RemoveAction(globalAction);
            }
        }

        [TestMethod]
        public void GetDefinedAction_DisabledAction_NoGlobal_ReturnsEmpty()
        {
            var action = CreateAction("SwipeUp", "gesture1", isEnabled: false);
            var app = CreateUserApp("TestApp", action);

            // useGlobal = false, so no fallback
            var result = ApplicationManager.Instance.GetDefinedAction("gesture1", new[] { app }, false);

            Assert.AreEqual(0, result.Count());
        }

        [TestMethod]
        public void GetDefinedAction_DisabledCommand_FiltersOut()
        {
            // Existing behavior: action with all commands disabled should be filtered
            var action = CreateAction("SwipeUp", "gesture1", isEnabled: true, commandEnabled: false);
            var app = CreateUserApp("TestApp", action);

            var result = ApplicationManager.Instance.GetDefinedAction("gesture1", new[] { app }, false);

            Assert.AreEqual(0, result.Count(), "Action with all commands disabled should be filtered out");
        }

        #endregion
    }
}
