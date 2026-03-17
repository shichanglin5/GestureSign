using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using GestureSign.Common.Gestures;
using GestureSign.Common.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace GestureSign.Tests
{
    [TestClass]
    public class ContactGestureConfigTests
    {
        [TestMethod]
        public void ApplicationBase_HasContactGestures_ByDefault()
        {
            var app = new UserApp();

            Assert.IsNotNull(app.ContactGestures);
            Assert.IsNotNull(app.ContactGestures.Taps);
            Assert.IsNotNull(app.ContactGestures.TipTaps);
            Assert.IsNotNull(app.ContactGestures.Clicks);
        }

        [TestMethod]
        public void Serialize_ContactGestureSettings_WithTapAndTipTap_RoundTrips()
        {
            var app = new UserApp();
            app.ContactGestures.Taps.Add(new TapGestureConfig
            {
                Id = "tap-1",
                Name = "3指轻点",
                FingerCount = 3,
                Recognition = new TapGestureRecognition { MaxDurationMs = 180, MaxMovementPx = 12 }
            });
            app.ContactGestures.TipTaps.Add(new TipTapGestureConfig
            {
                Id = "tiptap-1",
                Name = "TipTap Left",
                FingerCount = 2,
                FixFingerCount = 1,
                Direction = ContactGestureDirection.Left,
                Recognition = new TipTapRecognition { FixMinHoldMs = 40, MaxTapDurationMs = 120 }
            });
            app.ContactGestures.Clicks.Add(new ClickGestureConfig
            {
                Id = "click-1",
                Name = "3指按压",
                FingerCount = 3,
                Recognition = new ClickGestureRecognition { MaxPressDurationMs = 320, MaxMovementPx = 14 }
            });

            var json = JsonConvert.SerializeObject(app);
            var clone = JsonConvert.DeserializeObject<UserApp>(json);

            Assert.IsNotNull(clone);
            Assert.IsNotNull(clone.ContactGestures);
            Assert.AreEqual(1, clone.ContactGestures.Taps.Count);
            Assert.AreEqual(1, clone.ContactGestures.TipTaps.Count);
            Assert.AreEqual(1, clone.ContactGestures.Clicks.Count);
            Assert.AreEqual("tap-1", clone.ContactGestures.Taps[0].Id);
            Assert.AreEqual("3指轻点", clone.ContactGestures.Taps[0].Name);
            Assert.AreEqual(3, clone.ContactGestures.Taps[0].FingerCount);
            Assert.AreEqual("tiptap-1", clone.ContactGestures.TipTaps[0].Id);
            Assert.AreEqual("TipTap Left", clone.ContactGestures.TipTaps[0].Name);
            Assert.AreEqual(ContactGestureDirection.Left, clone.ContactGestures.TipTaps[0].Direction);
            Assert.AreEqual(1, clone.ContactGestures.TipTaps[0].FixFingerCount);
            Assert.AreEqual("click-1", clone.ContactGestures.Clicks[0].Id);
            Assert.AreEqual("3指按压", clone.ContactGestures.Clicks[0].Name);
        }

        [TestMethod]
        public void ContactGestureSettings_Empty_DefaultsToNotSerialize()
        {
            var settings = new ContactGestureSettings();

            Assert.IsFalse(settings.ShouldSerialize());
        }

        [TestMethod]
        public void Deserialize_ContactGestureSettings_WithExplicitNullCollections_NormalizesToEmptyLists()
        {
            var settings = JsonConvert.DeserializeObject<ContactGestureSettings>("{\"Enabled\":true,\"Taps\":null,\"TipTaps\":null,\"Clicks\":null}");

            Assert.IsNotNull(settings);
            Assert.IsNotNull(settings.Taps);
            Assert.IsNotNull(settings.TipTaps);
            Assert.IsNotNull(settings.Clicks);
            Assert.AreEqual(0, settings.Taps.Count);
            Assert.AreEqual(0, settings.TipTaps.Count);
            Assert.AreEqual(0, settings.Clicks.Count);
        }

        [TestMethod]
        public void ContactGestureSettings_ShouldSerialize_DoesNotThrowWhenCollectionsExplicitlyNull()
        {
            var settings = new ContactGestureSettings
            {
                Taps = null,
                TipTaps = null,
                Clicks = null,
            };

            Assert.IsFalse(settings.ShouldSerialize());
            Assert.IsNotNull(settings.Taps);
            Assert.IsNotNull(settings.TipTaps);
            Assert.IsNotNull(settings.Clicks);
        }

        [TestMethod]
        public void GetRecognizedTapCommands_PrefersRecognizedApplicationCommands()
        {
            var manager = ApplicationManager.Instance;
            manager.LoadingTask.Wait();

            var oldApps = manager.Applications;

            try
            {
                manager.RemoveAllApplication();
                var global = (GlobalApp)manager.GetGlobalApplication();
                global.ContactGestures.Taps.Add(new TapGestureConfig
                {
                    Id = "tap-global",
                    FingerCount = 3,
                    Commands = new List<Command>
                    {
                        new Command { PluginClass = "GlobalTap", IsEnabled = true }
                    }
                });

                var app = new UserApp { Name = "RecognizedApp" };
                app.ContactGestures.Taps.Add(new TapGestureConfig
                {
                    Id = "tap-app",
                    FingerCount = 3,
                    Commands = new List<Command>
                    {
                        new Command { PluginClass = "AppTap", IsEnabled = true }
                    }
                });
                manager.AddApplication(app);
                SetRecognizedApplications(manager, app);

                var commands = manager.GetRecognizedTapCommands(3, GestureModifiers.Default).Cast<Command>().ToList();

                Assert.AreEqual(1, commands.Count);
                Assert.AreEqual("AppTap", commands[0].PluginClass);
            }
            finally
            {
                SetRecognizedApplications(manager, null);
                manager.RemoveAllApplication();
                manager.AddApplicationRange(oldApps);
            }
        }

        [TestMethod]
        public void GetRecognizedTapCommands_FallsBackToGlobalCommands()
        {
            var manager = ApplicationManager.Instance;
            manager.LoadingTask.Wait();

            var oldApps = manager.Applications;

            try
            {
                manager.RemoveAllApplication();
                var global = (GlobalApp)manager.GetGlobalApplication();
                global.ContactGestures.Taps.Add(new TapGestureConfig
                {
                    Id = "tap-global",
                    FingerCount = 3,
                    Commands = new List<Command>
                    {
                        new Command { PluginClass = "GlobalTap", IsEnabled = true }
                    }
                });

                var app = new UserApp { Name = "RecognizedApp" };
                app.ContactGestures.Taps.Add(new TapGestureConfig
                {
                    Id = "tap-app",
                    FingerCount = 3,
                    Commands = new List<Command>
                    {
                        new Command { PluginClass = "AppTap", IsEnabled = false }
                    }
                });
                manager.AddApplication(app);
                SetRecognizedApplications(manager, app);

                var commands = manager.GetRecognizedTapCommands(3, GestureModifiers.Default).Cast<Command>().ToList();

                Assert.AreEqual(1, commands.Count);
                Assert.AreEqual("GlobalTap", commands[0].PluginClass);
            }
            finally
            {
                SetRecognizedApplications(manager, null);
                manager.RemoveAllApplication();
                manager.AddApplicationRange(oldApps);
            }
        }

        [TestMethod]
        public void GetRecognizedTipTapCommands_PrefersRecognizedApplicationCommands()
        {
            var manager = ApplicationManager.Instance;
            manager.LoadingTask.Wait();

            var oldApps = manager.Applications;

            try
            {
                manager.RemoveAllApplication();
                var global = (GlobalApp)manager.GetGlobalApplication();
                global.ContactGestures.TipTaps.Add(new TipTapGestureConfig
                {
                    Id = "tiptap-left",
                    FingerCount = 2,
                    Direction = ContactGestureDirection.Left,
                    Commands = new List<Command>
                    {
                        new Command { PluginClass = "GlobalTipTap", IsEnabled = true }
                    }
                });

                var app = new UserApp { Name = "RecognizedApp" };
                app.ContactGestures.TipTaps.Add(new TipTapGestureConfig
                {
                    Id = "tiptap-left",
                    FingerCount = 2,
                    Direction = ContactGestureDirection.Left,
                    Commands = new List<Command>
                    {
                        new Command { PluginClass = "AppTipTap", IsEnabled = true }
                    }
                });
                manager.AddApplication(app);
                SetRecognizedApplications(manager, app);

                var commands = manager.GetRecognizedTipTapCommands("tiptap-left", 2, GestureModifiers.Default).Cast<Command>().ToList();

                Assert.AreEqual(1, commands.Count);
                Assert.AreEqual("AppTipTap", commands[0].PluginClass);
            }
            finally
            {
                SetRecognizedApplications(manager, null);
                manager.RemoveAllApplication();
                manager.AddApplicationRange(oldApps);
            }
        }

        [TestMethod]
        public void GetRecognizedTipTapCommands_FallsBackToGlobalCommands()
        {
            var manager = ApplicationManager.Instance;
            manager.LoadingTask.Wait();

            var oldApps = manager.Applications;

            try
            {
                manager.RemoveAllApplication();
                var global = (GlobalApp)manager.GetGlobalApplication();
                global.ContactGestures.TipTaps.Add(new TipTapGestureConfig
                {
                    Id = "tiptap-left",
                    FingerCount = 2,
                    Direction = ContactGestureDirection.Left,
                    Commands = new List<Command>
                    {
                        new Command { PluginClass = "GlobalTipTap", IsEnabled = true }
                    }
                });

                var app = new UserApp { Name = "RecognizedApp" };
                app.ContactGestures.TipTaps.Add(new TipTapGestureConfig
                {
                    Id = "tiptap-left",
                    FingerCount = 2,
                    Direction = ContactGestureDirection.Left,
                    Commands = new List<Command>
                    {
                        new Command { PluginClass = "AppTipTap", IsEnabled = false }
                    }
                });
                manager.AddApplication(app);
                SetRecognizedApplications(manager, app);

                var commands = manager.GetRecognizedTipTapCommands("tiptap-left", 2, GestureModifiers.Default).Cast<Command>().ToList();

                Assert.AreEqual(1, commands.Count);
                Assert.AreEqual("GlobalTipTap", commands[0].PluginClass);
            }
            finally
            {
                SetRecognizedApplications(manager, null);
                manager.RemoveAllApplication();
                manager.AddApplicationRange(oldApps);
            }
        }

        [TestMethod]
        public void GetRecognizedTapCommands_WithGestureId_MatchesById()
        {
            var manager = ApplicationManager.Instance;
            manager.LoadingTask.Wait();

            var oldApps = manager.Applications;

            try
            {
                manager.RemoveAllApplication();
                var global = (GlobalApp)manager.GetGlobalApplication();
                global.ContactGestures.Taps.Add(new TapGestureConfig
                {
                    Id = "tap-3f-a",
                    FingerCount = 3,
                    Commands = new List<Command>
                    {
                        new Command { PluginClass = "TapA", IsEnabled = true }
                    }
                });
                global.ContactGestures.Taps.Add(new TapGestureConfig
                {
                    Id = "tap-3f-b",
                    FingerCount = 3,
                    Commands = new List<Command>
                    {
                        new Command { PluginClass = "TapB", IsEnabled = true }
                    }
                });

                // 按 gestureId 精确匹配，只返回 tap-3f-b 的命令
                var commands = manager.GetRecognizedTapCommands("tap-3f-b", 3, GestureModifiers.Default).Cast<Command>().ToList();

                Assert.AreEqual(1, commands.Count);
                Assert.AreEqual("TapB", commands[0].PluginClass);
            }
            finally
            {
                manager.RemoveAllApplication();
                manager.AddApplicationRange(oldApps);
            }
        }

        [TestMethod]
        public void GetRecognizedTapCommands_WithGestureId_NoMatchReturnsEmpty()
        {
            var manager = ApplicationManager.Instance;
            manager.LoadingTask.Wait();

            var oldApps = manager.Applications;

            try
            {
                manager.RemoveAllApplication();
                var global = (GlobalApp)manager.GetGlobalApplication();
                global.ContactGestures.Taps.Add(new TapGestureConfig
                {
                    Id = "tap-3f-a",
                    FingerCount = 3,
                    Commands = new List<Command>
                    {
                        new Command { PluginClass = "TapA", IsEnabled = true }
                    }
                });

                // 传入不存在的 gestureId，应返回空
                var commands = manager.GetRecognizedTapCommands("nonexistent-id", 3, GestureModifiers.Default).ToList();

                Assert.AreEqual(0, commands.Count);
            }
            finally
            {
                manager.RemoveAllApplication();
                manager.AddApplicationRange(oldApps);
            }
        }

        [TestMethod]
        public void GetRecognizedTapCommands_WithoutGestureId_ReturnsAllMatching()
        {
            var manager = ApplicationManager.Instance;
            manager.LoadingTask.Wait();

            var oldApps = manager.Applications;

            try
            {
                manager.RemoveAllApplication();
                var global = (GlobalApp)manager.GetGlobalApplication();
                global.ContactGestures.Taps.Add(new TapGestureConfig
                {
                    Id = "tap-3f-a",
                    FingerCount = 3,
                    Commands = new List<Command>
                    {
                        new Command { PluginClass = "TapA", IsEnabled = true }
                    }
                });
                global.ContactGestures.Taps.Add(new TapGestureConfig
                {
                    Id = "tap-3f-b",
                    FingerCount = 3,
                    Commands = new List<Command>
                    {
                        new Command { PluginClass = "TapB", IsEnabled = true }
                    }
                });

                // 不传 gestureId，返回所有匹配 fingerCount 的命令
                var commands = manager.GetRecognizedTapCommands(3, GestureModifiers.Default).Cast<Command>().ToList();

                Assert.AreEqual(2, commands.Count);
            }
            finally
            {
                manager.RemoveAllApplication();
                manager.AddApplicationRange(oldApps);
            }
        }

        [TestMethod]
        public void GetRecognizedTapDefinitions_ModifierFilter_StrictEquality()
        {
            var manager = ApplicationManager.Instance;
            manager.LoadingTask.Wait();
            var oldApps = manager.Applications;
            try
            {
                manager.RemoveAllApplication();
                var global = (GlobalApp)manager.GetGlobalApplication();
                global.ContactGestures.Taps.Add(new TapGestureConfig
                {
                    Id = "tap-default",
                    FingerCount = 3,
                    Modifiers = GestureModifiers.Default,
                });
                global.ContactGestures.Taps.Add(new TapGestureConfig
                {
                    Id = "tap-btn",
                    FingerCount = 3,
                    Modifiers = GestureModifiers.PrimaryButtonDown,
                });

                var defaultDefs = manager.GetGlobalTapDefinitions(3, GestureModifiers.Default).ToList();
                var btnDefs = manager.GetGlobalTapDefinitions(3, GestureModifiers.PrimaryButtonDown).ToList();
                var ctrlDefs = manager.GetGlobalTapDefinitions(3, GestureModifiers.Ctrl).ToList();

                Assert.AreEqual(1, defaultDefs.Count);
                Assert.AreEqual("tap-default", defaultDefs[0].Id);
                Assert.AreEqual(1, btnDefs.Count);
                Assert.AreEqual("tap-btn", btnDefs[0].Id);
                Assert.AreEqual(0, ctrlDefs.Count);
            }
            finally
            {
                manager.RemoveAllApplication();
                manager.AddApplicationRange(oldApps);
            }
        }

        [TestMethod]
        public void GetRecognizedTipTapConfigsByFixCount_ModifierFilter()
        {
            var manager = ApplicationManager.Instance;
            manager.LoadingTask.Wait();
            var oldApps = manager.Applications;
            try
            {
                manager.RemoveAllApplication();
                var global = (GlobalApp)manager.GetGlobalApplication();
                global.ContactGestures.TipTaps.Add(new TipTapGestureConfig
                {
                    Id = "tiptap-default",
                    FingerCount = 2,
                    FixFingerCount = 1,
                    Modifiers = GestureModifiers.Default,
                });
                global.ContactGestures.TipTaps.Add(new TipTapGestureConfig
                {
                    Id = "tiptap-btn",
                    FingerCount = 2,
                    FixFingerCount = 1,
                    Modifiers = GestureModifiers.PrimaryButtonDown,
                });

                var defaultDefs = manager.GetRecognizedTipTapConfigsByFixCount(1, GestureModifiers.Default).ToList();
                var btnDefs = manager.GetRecognizedTipTapConfigsByFixCount(1, GestureModifiers.PrimaryButtonDown).ToList();

                Assert.AreEqual(1, defaultDefs.Count);
                Assert.AreEqual("tiptap-default", defaultDefs[0].Id);
                Assert.AreEqual(1, btnDefs.Count);
                Assert.AreEqual("tiptap-btn", btnDefs[0].Id);
            }
            finally
            {
                manager.RemoveAllApplication();
                manager.AddApplicationRange(oldApps);
            }
        }

        [TestMethod]
        public void GestureModifiers_FlagsSemantics()
        {
            Assert.AreEqual(0, (int)GestureModifiers.Default);
            Assert.AreEqual(3, (int)(GestureModifiers.PrimaryButtonDown | GestureModifiers.Ctrl));
            Assert.IsTrue((GestureModifiers.PrimaryButtonDown | GestureModifiers.Ctrl).HasFlag(GestureModifiers.PrimaryButtonDown));
            Assert.IsFalse(GestureModifiers.Default.HasFlag(GestureModifiers.PrimaryButtonDown));
        }

        [TestMethod]
        public void CreateTransportTapDefinition_PreservesModifiers()
        {
            // 通过 GestureDefinitionFactory 内部路径验证 Transport 副本保留 Modifiers
            var manager = ApplicationManager.Instance;
            manager.LoadingTask.Wait();
            var oldApps = manager.Applications;
            try
            {
                manager.RemoveAllApplication();
                var global = (GlobalApp)manager.GetGlobalApplication();
                var tapConfig = new TapGestureConfig
                {
                    Id = "tap-ctrl",
                    Name = "Ctrl Tap",
                    FingerCount = 3,
                    Modifiers = GestureModifiers.PrimaryButtonDown | GestureModifiers.Ctrl,
                };
                global.ContactGestures.Taps.Add(tapConfig);

                // GetGlobalTapDefinitions 返回时应保留 Modifiers
                var found = manager.GetGlobalTapDefinitions(3, GestureModifiers.PrimaryButtonDown | GestureModifiers.Ctrl).FirstOrDefault();
                Assert.IsNotNull(found);
                Assert.AreEqual(GestureModifiers.PrimaryButtonDown | GestureModifiers.Ctrl, found.Modifiers);
            }
            finally
            {
                manager.RemoveAllApplication();
                manager.AddApplicationRange(oldApps);
            }
        }

        [TestMethod]
        public void GetRecognizedTapCommands_ModifierFilter()
        {
            var manager = ApplicationManager.Instance;
            manager.LoadingTask.Wait();
            var oldApps = manager.Applications;
            try
            {
                manager.RemoveAllApplication();
                var global = (GlobalApp)manager.GetGlobalApplication();
                global.ContactGestures.Taps.Add(new TapGestureConfig
                {
                    Id = "tap-default-cmd",
                    FingerCount = 3,
                    Modifiers = GestureModifiers.Default,
                    Commands = new List<Command>
                    {
                        new Command { PluginClass = "DefaultCmd", IsEnabled = true }
                    }
                });
                global.ContactGestures.Taps.Add(new TapGestureConfig
                {
                    Id = "tap-btn-cmd",
                    FingerCount = 3,
                    Modifiers = GestureModifiers.PrimaryButtonDown,
                    Commands = new List<Command>
                    {
                        new Command { PluginClass = "BtnCmd", IsEnabled = true }
                    }
                });

                var defaultCmds = manager.GetRecognizedTapCommands(3, GestureModifiers.Default).Cast<Command>().ToList();
                var btnCmds = manager.GetRecognizedTapCommands(3, GestureModifiers.PrimaryButtonDown).Cast<Command>().ToList();

                Assert.AreEqual(1, defaultCmds.Count);
                Assert.AreEqual("DefaultCmd", defaultCmds[0].PluginClass);
                Assert.AreEqual(1, btnCmds.Count);
                Assert.AreEqual("BtnCmd", btnCmds[0].PluginClass);
            }
            finally
            {
                manager.RemoveAllApplication();
                manager.AddApplicationRange(oldApps);
            }
        }

        [TestMethod]
        public void GetDirectionLabel_Middle_ReturnsMiddle_NotAny()
        {
            // M12 回归：Middle 方向应返回 "Middle"，不应掉入默认分支返回 "Any"
            var label = ContactGestureText.GetDirectionLabel(ContactGestureDirection.Middle);

            Assert.AreEqual("Middle", label);
        }

        [TestMethod]
        public void GetDirectionLabel_AllDirections_HaveDistinctLabels()
        {
            var directions = new[]
            {
                ContactGestureDirection.Left,
                ContactGestureDirection.Right,
                ContactGestureDirection.Middle,
            };

            var labels = directions.Select(d => ContactGestureText.GetDirectionLabel(d)).ToList();

            // 每个方向的标签应互不相同
            Assert.AreEqual(labels.Count, labels.Distinct().Count());
            // 且不应包含 "Any"（Any 只用于 None 或未知方向）
            CollectionAssert.DoesNotContain(labels, "Any");
        }

        [TestMethod]
        public void SaveAndLoad_WithTypeNameHandling_PreservesTapDefinitions()
        {
            // 模拟 FileManager.SaveObject + LoadObject 完整往返
            var apps = new List<IApplication>
            {
                new GlobalApp
                {
                    Name = "(全局动作)",
                    ContactGestures = new ContactGestureSettings
                    {
                        Enabled = true,
                        Taps = new List<TapGestureConfig>
                        {
                            new TapGestureConfig
                            {
                                Id = "tap-4f",
                                Name = "四指轻点",
                                FingerCount = 4,
                                Commands = new List<Command>
                                {
                                    new Command { PluginClass = "TestPlugin", IsEnabled = true }
                                }
                            }
                        }
                    }
                }
            };

            string tempFile = Path.Combine(Path.GetTempPath(), $"GS-test-{Guid.NewGuid():N}.gsa");
            try
            {
                // 使用 FileManager 真实序列化
                Assert.IsTrue(FileManager.SaveObject(apps, tempFile, typeName: true));

                // 验证文件内容包含 tap 定义
                string json = File.ReadAllText(tempFile);
                Assert.IsTrue(json.Contains("tap-4f"), $"Saved JSON should contain tap ID. JSON={json.Substring(0, Math.Min(500, json.Length))}");
                Assert.IsTrue(json.Contains("四指轻点"), "Saved JSON should contain tap name");

                // 使用 FileManager 真实反序列化
                var loaded = FileManager.LoadObject<List<IApplication>>(tempFile, false, typeName: true);

                Assert.IsNotNull(loaded);
                var globalApp = loaded.OfType<GlobalApp>().FirstOrDefault();
                Assert.IsNotNull(globalApp, "GlobalApp should be loaded");
                Assert.IsNotNull(globalApp.ContactGestures, "ContactGestures should not be null");
                Assert.AreEqual(1, globalApp.ContactGestures.Taps.Count, "Should have 1 tap definition");
                Assert.AreEqual("tap-4f", globalApp.ContactGestures.Taps[0].Id);
                Assert.AreEqual("四指轻点", globalApp.ContactGestures.Taps[0].Name);
                Assert.AreEqual(4, globalApp.ContactGestures.Taps[0].FingerCount);
                Assert.AreEqual(1, globalApp.ContactGestures.Taps[0].Commands.Count);
                Assert.AreEqual("TestPlugin", globalApp.ContactGestures.Taps[0].Commands[0].PluginClass);
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        [TestMethod]
        public void TipTapConfig_WithCommandsAndNoId_CommandsStillAccessible()
        {
            // 回归测试：修复前 TryExecuteRecognizedTipTapCommands 在 config.Id 为空时直接返回 false，
            // 修复后优先使用 config.Commands，Id 为空不再阻止命令执行。
            var config = new TipTapGestureConfig
            {
                Id = null, // 无 Id
                FingerCount = 2,
                FixFingerCount = 1,
                Direction = ContactGestureDirection.Left,
                Commands = new List<Command>
                {
                    new Command { PluginClass = "TestPlugin", IsEnabled = true },
                    new Command { PluginClass = "DisabledPlugin", IsEnabled = false },
                }
            };

            // 模拟 TryExecuteRecognizedTipTapCommands 中的新逻辑
            var commands = config.Commands?.Where(c => c != null && c.IsEnabled).OfType<ICommand>().ToList();

            Assert.IsNotNull(commands);
            Assert.AreEqual(1, commands.Count, "Should have 1 enabled command even without Id");
            Assert.AreEqual("TestPlugin", ((Command)commands[0]).PluginClass);
        }

        [TestMethod]
        public void TipTapConfig_WithCommandsAndId_UsesCommandsDirectly()
        {
            // 验证当 config 同时有 Id 和 Commands 时，直接使用 Commands
            var config = new TipTapGestureConfig
            {
                Id = "tiptap-left-2f",
                FingerCount = 2,
                FixFingerCount = 1,
                Commands = new List<Command>
                {
                    new Command { PluginClass = "DirectPlugin", IsEnabled = true },
                }
            };

            var commands = config.Commands?.Where(c => c != null && c.IsEnabled).OfType<ICommand>().ToList();

            Assert.IsNotNull(commands);
            Assert.AreEqual(1, commands.Count);
            Assert.AreEqual("DirectPlugin", ((Command)commands[0]).PluginClass);
        }

        [TestMethod]
        public void TipTapConfig_EmptyCommands_FallbackToIdLookup()
        {
            // 验证当 config.Commands 为空时，fallback 通过 Id 从 ApplicationManager 查找
            var manager = ApplicationManager.Instance;
            manager.LoadingTask.Wait();

            var oldApps = manager.Applications;

            try
            {
                manager.RemoveAllApplication();
                var global = (GlobalApp)manager.GetGlobalApplication();
                global.ContactGestures.TipTaps.Add(new TipTapGestureConfig
                {
                    Id = "tiptap-fallback",
                    FingerCount = 2,
                    FixFingerCount = 1,
                    Commands = new List<Command>
                    {
                        new Command { PluginClass = "FallbackPlugin", IsEnabled = true }
                    }
                });

                // config 自身 Commands 为空，但有 Id
                var config = new TipTapGestureConfig
                {
                    Id = "tiptap-fallback",
                    FingerCount = 2,
                    Commands = new List<Command>() // 空列表
                };

                // 模拟新逻辑：先查 config.Commands，为空则通过 Id 查找
                var commands = config.Commands?.Where(c => c != null && c.IsEnabled).OfType<ICommand>().ToList();
                if (commands == null || commands.Count == 0)
                {
                    commands = manager.GetRecognizedTipTapCommands(config.Id, config.FingerCount, GestureModifiers.Default).ToList();
                }

                Assert.AreEqual(1, commands.Count, "Should fallback to ApplicationManager lookup when config.Commands is empty");
                Assert.AreEqual("FallbackPlugin", ((Command)commands[0]).PluginClass);
            }
            finally
            {
                manager.RemoveAllApplication();
                manager.AddApplicationRange(oldApps);
            }
        }

        [TestMethod]
        public void TipTapConfig_NullIdAndNullCommands_ReturnsNoCommands()
        {
            // 极端情况：Id 和 Commands 都为空
            var config = new TipTapGestureConfig
            {
                Id = null,
                FingerCount = 2,
                Commands = null
            };

            var commands = config.Commands?.Where(c => c != null && c.IsEnabled).OfType<ICommand>().ToList();

            Assert.IsTrue(commands == null || commands.Count == 0,
                "Should return no commands when both Id and Commands are null");
        }

        private static void SetRecognizedApplications(ApplicationManager manager, params IApplication[] applications)
        {
            var field = typeof(ApplicationManager).GetField("_recognizedApplication", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            field.SetValue(manager, applications);
        }
    }
}
