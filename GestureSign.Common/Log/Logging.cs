using System;
using System.IO;
using GestureSign.Common.Configuration;

namespace GestureSign.Common.Log
{
    public enum LogLevel
    {
        Error = 0,      // Only errors and exceptions
        Warning = 1,    // Warnings and errors
        Info = 2,       // Important information, warnings and errors
        Debug = 3,      // Debug information (default for development)
        Trace = 4       // Detailed trace information (very verbose)
    }

    public class Logging
    {
        private static string _logFilePath;
        private static StreamWriterWithTimestamp _logWriter;
        private static LogLevel _currentLogLevel = LogLevel.Info;

        public static LogLevel CurrentLogLevel
        {
            get => _currentLogLevel;
            set => _currentLogLevel = value;
        }

        private class StreamWriterWithTimestamp : StreamWriter
        {
            public StreamWriterWithTimestamp(Stream stream) : base(stream)
            {
            }

            private string GetTimestamp()
            {
                return "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] ";
            }

            private string GetNameAndVersion()
            {
                var assemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName();
                return $"[{assemblyName.Name} v{assemblyName.Version}] ";
            }

            public void WriteLineWithLevel(string value, LogLevel level)
            {
                string levelPrefix = level switch
                {
                    LogLevel.Error => "[ERROR] ",
                    LogLevel.Warning => "[WARN] ",
                    LogLevel.Info => "[INFO] ",
                    LogLevel.Debug => "[DEBUG] ",
                    LogLevel.Trace => "[TRACE] ",
                    _ => ""
                };
                base.WriteLine(GetTimestamp() + GetNameAndVersion() + levelPrefix + value);
            }

            public override void WriteLine(string value)
            {
                base.WriteLine(GetTimestamp() + GetNameAndVersion() + value);
            }

            public override void Write(string value)
            {
                base.Write(GetTimestamp() + GetNameAndVersion() + value);
            }
        }

        public static string LogFilePath => _logFilePath;
        public static event EventHandler<Exception> LoggedExceptionOccurred;

        public static bool OpenLogFile()
        {
            bool result;
            try
            {
                _logFilePath = Path.Combine(AppConfig.LocalApplicationDataPath, "GestureSign.log");
                CheckLogSize(_logFilePath);
                _logWriter = new StreamWriterWithTimestamp(new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
                Console.SetOut(_logWriter);
                Console.SetError(_logWriter);

                // Initialize log level from config
                _currentLogLevel = AppConfig.LogLevel;

                result = true;
            }
            catch (Exception e)
            {
                LogAndNotice(e);
                result = false;
            }
            return result;
        }

        public static void LogException(Exception e)
        {
            if (!(e is ObjectDisposedException))
            {
                Console.WriteLine(e);
                Console.WriteLine();
                if (e.InnerException != null)
                    LogException(e.InnerException);
            }
        }

        public static void LogAndNotice(Exception e)
        {
            LogException(e);
            LoggedExceptionOccurred?.Invoke(null, e);
        }

        public static void LogMessage(string message)
        {
            Console.WriteLine(message);
            Console.WriteLine();
        }

        // Log with level - only logs if current level >= message level
        public static void Log(string message, LogLevel level)
        {
            if (_currentLogLevel >= level && _logWriter != null)
            {
                _logWriter.WriteLineWithLevel(message, level);
                _logWriter.WriteLine();
            }
        }

        // Convenience methods for different log levels
        public static void LogError(string message) => Log(message, LogLevel.Error);
        public static void LogWarning(string message) => Log(message, LogLevel.Warning);
        public static void LogInfo(string message) => Log(message, LogLevel.Info);
        public static void LogDebug(string message) => Log(message, LogLevel.Debug);
        public static void LogTrace(string message) => Log(message, LogLevel.Trace);

        private static void CheckLogSize(string logPath)
        {
            if (File.Exists(logPath))
            {
                if (new FileInfo(logPath).Length > 102400)
                    File.Delete(logPath);
            }
        }
    }
}
