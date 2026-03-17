using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using GestureSign.CorePlugins.ActivateApp;
using ManagedWinapi.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Reflection;

namespace GestureSign.Tests
{
    [TestClass]
    public class ActivateAppPluginTests
    {
        [TestMethod]
        public void ResolveActivationMode_GlobalSafeModeDefault_ReturnsSafeMode()
        {
            var method = typeof(ActivateAppPlugin).GetMethod("ResolveActivationMode", BindingFlags.Instance | BindingFlags.NonPublic);
            var plugin = new ActivateAppPlugin();
            int originalDefault = AppConfig.DefaultActivationMethod;

            Assert.IsNotNull(method);

            try
            {
                AppConfig.DefaultActivationMethod = (int)ActivationMethod.SafeMode;

                var result = (WindowActivationMode)method.Invoke(plugin, new object[]
                {
                    new ActivateAppSettings { ActivationMethod = ActivationMethod.UseGlobal }
                });

                Assert.AreEqual(WindowActivationMode.SafeMode, result);
            }
            finally
            {
                AppConfig.DefaultActivationMethod = originalDefault;
            }
        }

        [TestMethod]
        public void ResolveActivationMode_PresetOverride_TakesPriorityOverGlobalDefault()
        {
            var method = typeof(ActivateAppPlugin).GetMethod("ResolveActivationMode", BindingFlags.Instance | BindingFlags.NonPublic);
            var plugin = new ActivateAppPlugin();
            int originalDefault = AppConfig.DefaultActivationMethod;
            var presetManager = WindowPresetManager.Instance;
            var originalPresets = presetManager.Presets.ToArray();

            Assert.IsNotNull(method);

            try
            {
                presetManager.Presets.Clear();
                presetManager.AddPreset(new WindowRule
                {
                    Id = "preset-safe",
                    Name = "PresetSafe",
                    ActivationMethod = ActivationMethod.AttachThreadInput,
                });
                AppConfig.DefaultActivationMethod = (int)ActivationMethod.SafeMode;

                var result = (WindowActivationMode)method.Invoke(plugin, new object[]
                {
                    new ActivateAppSettings
                    {
                        PresetId = "preset-safe",
                        ActivationMethod = ActivationMethod.UseGlobal,
                    }
                });

                Assert.AreEqual(WindowActivationMode.AttachThreadInput, result);
            }
            finally
            {
                presetManager.Presets.Clear();
                foreach (var preset in originalPresets)
                {
                    presetManager.Presets.Add(preset);
                }

                AppConfig.DefaultActivationMethod = originalDefault;
            }
        }
    }
}
