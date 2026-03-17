using GestureSign.Common.Applications;
using GestureSign.Common.Gestures;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace GestureSign.Tests
{
    [TestClass]
    public class GestureIdBindingTests
    {
        [TestMethod]
        public void Action_CanStoreGestureId_SeparatelyFromGestureName()
        {
            var action = new GestureSign.Common.Applications.Action
            {
                GestureId = "gesture-id-1",
                GestureName = "OldName"
            };

            Assert.AreEqual("gesture-id-1", action.GestureId);
            Assert.AreEqual("OldName", action.GestureName);
        }

        [TestMethod]
        public void GetDefinedAction_PrefersGestureIdWhenProvided()
        {
            var action = new GestureSign.Common.Applications.Action
            {
                Name = "ById",
                GestureId = "g1",
                GestureName = "OldName"
            };
            action.AddCommand(new Command { PluginClass = "TestPlugin", IsEnabled = true });

            var app = new UserApp { Name = "TestApp" };
            app.AddAction(action);

            var result = ApplicationManager.Instance.GetDefinedAction("g1", new[] { app }, false).ToList();

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("ById", result[0].Name);
        }

        [TestMethod]
        public void Gesture_HasStableIdProperty()
        {
            var gesture = new Gesture { Id = "abc123", Name = "Demo" };

            Assert.AreEqual("abc123", gesture.Id);
            Assert.AreEqual("Demo", gesture.Name);
        }

        [TestMethod]
        public void GetDeterministicGestureId_WithoutPointPatterns_ReturnsStableLegacyId()
        {
            var method = typeof(GestureManager).GetMethod("GetDeterministicGestureId", BindingFlags.Static | BindingFlags.NonPublic);

            Assert.IsNotNull(method);

            string first = (string)method.Invoke(null, new object[] { "LegacyGesture", Array.Empty<PointPattern>(), 3 });
            string second = (string)method.Invoke(null, new object[] { "LegacyGesture", Array.Empty<PointPattern>(), 3 });

            Assert.AreEqual(first, second);
            StringAssert.StartsWith(first, "legacy_");
            Assert.AreEqual(15, first.Length);
            Assert.IsTrue(first.Skip(7).All(character => Uri.IsHexDigit(character)));
        }

        [TestMethod]
        public void ExecuteAfterTaskCompletion_CompletedTask_RunsActionImmediately()
        {
            var method = typeof(GestureManager).GetMethod("ExecuteAfterTaskCompletion", BindingFlags.Static | BindingFlags.NonPublic);
            bool invoked = false;

            Assert.IsNotNull(method);

            method.Invoke(null, new object[] { Task.CompletedTask, new System.Action(() => invoked = true) });

            Assert.IsTrue(invoked);
        }

        [TestMethod]
        public async Task ExecuteAfterTaskCompletion_IncompleteTask_DefersActionUntilTaskCompletes()
        {
            var method = typeof(GestureManager).GetMethod("ExecuteAfterTaskCompletion", BindingFlags.Static | BindingFlags.NonPublic);
            var dependency = new TaskCompletionSource<object>();
            var invoked = new TaskCompletionSource<bool>();

            Assert.IsNotNull(method);

            method.Invoke(null, new object[] { dependency.Task, new System.Action(() => invoked.TrySetResult(true)) });

            Assert.IsFalse(invoked.Task.IsCompleted);

            dependency.SetResult(null);

            var completed = await Task.WhenAny(invoked.Task, Task.Delay(1000));
            Assert.AreSame(invoked.Task, completed);
            Assert.IsTrue(await invoked.Task);
        }
    }
}
