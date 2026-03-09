using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using GestureSign.Common.Applications;
using GestureSign.Common.Log;
using Newtonsoft.Json;

namespace GestureSign.Common.Configuration
{
    public static class FileManager
    {
        #region Constructors

        static FileManager()
        {
        }

        #endregion

        #region Public Methods

        public static bool SaveObject(object serializableObject, string filePath, bool typeName = false, bool throwException = false)
        {
            try
            {
                string backup = null;
                if (File.Exists(filePath))
                {
                    backup = BackupFile(filePath);
                }

                using (var fs = OpenFileWithRetry(filePath, FileMode.Create, FileAccess.Write, FileShare.Read))
                using (var sWrite = new StreamWriter(fs))
                {
                    JsonSerializer serializer = new JsonSerializer
                    {
                        NullValueHandling = NullValueHandling.Ignore,
                        DefaultValueHandling = DefaultValueHandling.Include
                    };
                    if (typeName)
                    {
                        serializer.TypeNameHandling = TypeNameHandling.Objects;
                        serializer.TypeNameAssemblyFormat = System.Runtime.Serialization.Formatters.FormatterAssemblyStyle.Simple;
                    }
                    serializer.Serialize(sWrite, serializableObject);
                }

                if (File.Exists(backup))
                    File.Delete(backup);
                return true;
            }
            catch (Exception ex)
            {
                Logging.LogAndNotice(new Exceptions.FileWriteException(ex));
                if (throwException)
                    throw;
                return false;
            }
        }

        public static T LoadObject<T>(string filePath, bool backup, bool typeName = false, bool throwException = false)
        {
            try
            {
                if (!File.Exists(filePath)) return default(T);

                string json;
                using (var fs = OpenFileWithRetry(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(fs))
                {
                    json = reader.ReadToEnd();
                }

                return JsonConvert.DeserializeObject<T>(json, typeName
                    ? new JsonSerializerSettings()
                    {
                        TypeNameHandling = TypeNameHandling.Objects,
                        Converters = new List<JsonConverter>() { new ActionConverter(), new CommandConverter(), new WindowRuleConverter() }
                    }
                    : new JsonSerializerSettings());
            }
            catch (Exception e)
            {
                Logging.LogAndNotice(e);
                if (backup)
                    BackupFile(filePath);
                if (throwException)
                    throw;
                return default(T);
            }
        }

        public static void WaitFile(string filePath, int maxRetries = 10)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    using (new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                    return;
                }
                catch (IOException) when (i < maxRetries - 1)
                {
                    Thread.Sleep(50);
                }
                catch (FileNotFoundException)
                {
                    return;
                }
            }
        }

        private static FileStream OpenFileWithRetry(string filePath, FileMode mode, FileAccess access, FileShare share, int maxRetries = 10)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    return new FileStream(filePath, mode, access, share);
                }
                catch (IOException) when (i < maxRetries - 1)
                {
                    Thread.Sleep(50);
                }
            }
            return new FileStream(filePath, mode, access, share);
        }

        private static string BackupFile(string filePath)
        {
            try
            {
                var backupDirectory = new DirectoryInfo(AppConfig.BackupPath);
                if (!backupDirectory.Exists)
                    backupDirectory.Create();
                string backupFileName = Path.Combine(backupDirectory.FullName, DateTime.Now.ToString("yyMMddHHmmss") + Path.GetExtension(filePath));
                File.Copy(filePath, backupFileName, false);
                return backupFileName;
            }
            catch (Exception e)
            {
                Logging.LogException(e);
                return null;
            }
        }

        #endregion
    }
}
