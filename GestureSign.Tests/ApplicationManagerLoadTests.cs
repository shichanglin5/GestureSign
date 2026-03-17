using GestureSign.Common;
using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace GestureSign.Tests
{
    [TestClass]
    public class ApplicationManagerLoadTests
    {
        [TestMethod]
        public void LoadBackup_InvalidBackupFile_ReturnsFalse()
        {
            var manager = ApplicationManager.Instance;
            manager.LoadingTask.Wait();

            string tempRoot = Path.Combine(Path.GetTempPath(), "GestureSign-Tests", Guid.NewGuid().ToString("N"));
            string backupPath = Path.Combine(tempRoot, "Backup");
            Directory.CreateDirectory(backupPath);
            File.WriteAllText(Path.Combine(backupPath, Constants.ActionFileName), "{ invalid json }");

            string originalBackupPath = AppConfig.BackupPath;
            try
            {
                SetStaticProperty(typeof(AppConfig), nameof(AppConfig.BackupPath), backupPath);

                bool loaded = InvokePrivateLoad(manager, "LoadBackup");

                Assert.IsFalse(loaded);
            }
            finally
            {
                SetStaticProperty(typeof(AppConfig), nameof(AppConfig.BackupPath), originalBackupPath);
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, true);
            }
        }

        [TestMethod]
        public void LoadDefaults_InvalidDefaultsFile_ReturnsFalse()
        {
            var manager = ApplicationManager.Instance;
            manager.LoadingTask.Wait();

            string defaultsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Defaults");
            string defaultsFile = Path.Combine(defaultsDirectory, Constants.ActionFileName);
            string backupFile = defaultsFile + ".test-backup";
            Directory.CreateDirectory(defaultsDirectory);

            bool hadOriginalFile = File.Exists(defaultsFile);
            if (hadOriginalFile)
                File.Copy(defaultsFile, backupFile, true);

            File.WriteAllText(defaultsFile, "{ invalid json }");

            try
            {
                bool loaded = InvokePrivateLoad(manager, "LoadDefaults");

                Assert.IsFalse(loaded);
            }
            finally
            {
                if (hadOriginalFile)
                {
                    File.Copy(backupFile, defaultsFile, true);
                    File.Delete(backupFile);
                }
                else if (File.Exists(defaultsFile))
                {
                    File.Delete(defaultsFile);
                }
            }
        }

        [TestMethod]
        public void LoadLegacy_ValidLegacyFile_LoadsIntoSnapshot()
        {
            var manager = ApplicationManager.Instance;
            manager.LoadingTask.Wait();

            var oldApps = manager.Applications;
            string tempRoot = Path.Combine(Path.GetTempPath(), "GestureSign-Tests", Guid.NewGuid().ToString("N"));
            string applicationDataPath = Path.Combine(tempRoot, "AppData");
            Directory.CreateDirectory(applicationDataPath);

            string originalApplicationDataPath = AppConfig.ApplicationDataPath;
            try
            {
                SetStaticProperty(typeof(AppConfig), nameof(AppConfig.ApplicationDataPath), applicationDataPath);

                var legacyApps = new List<LegacyApplicationBase>
                {
                    new UserApplication
                    {
                        Name = "LegacyApp",
                        MatchUsing = MatchUsing.ExecutableFilename,
                        MatchString = "legacy.exe",
                        IsRegEx = false,
                        Group = "LegacyGroup",
                        LimitNumberOfFingers = 3,
                        BlockTouchInputThreshold = 4,
                    },
                    new IgnoredApplication("IgnoredApp", MatchUsing.WindowTitle, "Ignore Me", false, true),
                    new GlobalApplication(),
                };

                string legacyFilePath = Path.Combine(applicationDataPath, "Actions.act");
                Assert.IsTrue(FileManager.SaveObject(legacyApps, legacyFilePath, true));

                manager.RemoveAllApplication();
                bool loaded = InvokePrivateLoad(manager, "LoadLegacy");

                Assert.IsTrue(loaded);
                Assert.AreEqual(3, manager.Applications.Count);
                Assert.IsTrue(manager.Applications.OfType<UserApp>().Any(app => app.Name == "LegacyApp"));
                Assert.IsTrue(manager.Applications.OfType<IgnoredApp>().Any(app => app.Name == "IgnoredApp"));
                Assert.IsTrue(manager.Applications.OfType<GlobalApp>().Any());
            }
            finally
            {
                SetStaticProperty(typeof(AppConfig), nameof(AppConfig.ApplicationDataPath), originalApplicationDataPath);
                manager.RemoveAllApplication();
                manager.AddApplicationRange(oldApps);

                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, true);
            }
        }

        [TestMethod]
        public void MoveApplication_ReordersSnapshotInPlace()
        {
            var manager = ApplicationManager.Instance;
            manager.LoadingTask.Wait();

            var oldApps = manager.Applications;
            try
            {
                manager.RemoveAllApplication();

                var first = new UserApp { Name = "First" };
                var second = new UserApp { Name = "Second" };
                var third = new UserApp { Name = "Third" };

                manager.AddApplication(first);
                manager.AddApplication(second);
                manager.AddApplication(third);

                manager.MoveApplication(0, 2);

                CollectionAssert.AreEqual(
                    new[] { "Second", "Third", "First" },
                    manager.Applications.Select(app => app.Name).ToArray());
            }
            finally
            {
                manager.RemoveAllApplication();
                manager.AddApplicationRange(oldApps);
            }
        }

        private static bool InvokePrivateLoad(ApplicationManager manager, string methodName)
        {
            var method = typeof(ApplicationManager).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            return (bool)method.Invoke(manager, Array.Empty<object>());
        }

        private static void SetStaticProperty(Type type, string propertyName, object value)
        {
            var property = type.GetProperty(propertyName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(property);
            property.SetValue(null, value);
        }
    }
}
