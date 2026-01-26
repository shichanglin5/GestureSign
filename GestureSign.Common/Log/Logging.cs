using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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

        // Async logging queue
        private static BlockingCollection<string> _logQueue;
        private static Task _logTask;
        private static CancellationTokenSource _cancellationTokenSource;

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

        /// <summary>
        /// Opens the log file for writing
        /// </summary>
        /// <param name="redirectToStd">If true, outputs to console; otherwise outputs to file</param>
        /// <returns>True if successful, false otherwise</returns>
        public static bool OpenLogFile(bool redirectToStd = false)
        {
            bool result;
            try
            {
                if (redirectToStd)
                {
                    // Console mode: log to console window (synchronous)
                    _logFilePath = null;
                    _logWriter = null;
                    Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Logging to Console");
                }
                else
                {
                    // File mode (default behavior) with async logging
                    _logFilePath = Path.Combine(AppConfig.LocalApplicationDataPath, "GestureSign.log");
                    CheckLogSize(_logFilePath);
                    _logWriter = new StreamWriterWithTimestamp(new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };

                    // Start async logging task
                    _logQueue = new BlockingCollection<string>(boundedCapacity: 1000);
                    _cancellationTokenSource = new CancellationTokenSource();
                    _logTask = Task.Run(() => ProcessLogQueue(_cancellationTokenSource.Token));

                    Console.SetOut(new AsyncConsoleWriter(_logQueue));
                    Console.SetError(new AsyncConsoleWriter(_logQueue));
                }

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

        /// <summary>
        /// Process log messages from the queue asynchronously
        /// </summary>
        private static void ProcessLogQueue(CancellationToken cancellationToken)
        {
            try
            {
                foreach (var message in _logQueue.GetConsumingEnumerable(cancellationToken))
                {
                    if (_logWriter != null && !string.IsNullOrEmpty(message))
                    {
                        _logWriter.BaseStream.Write(System.Text.Encoding.UTF8.GetBytes(message));
                        _logWriter.BaseStream.Flush();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when shutting down
            }
            catch (Exception ex)
            {
                // Log to console as fallback
                System.Diagnostics.Debug.WriteLine($"[Logging] Error in ProcessLogQueue: {ex}");
            }
        }

        /// <summary>
        /// Shutdown async logging gracefully
        /// </summary>
        public static void Shutdown()
        {
            if (_logQueue != null)
            {
                _logQueue.CompleteAdding();
                _cancellationTokenSource?.Cancel();
                _logTask?.Wait(TimeSpan.FromSeconds(2));
                _logQueue?.Dispose();
                _cancellationTokenSource?.Dispose();
            }
            _logWriter?.Dispose();
        }

        /// <summary>
        /// Custom TextWriter that queues messages for async writing
        /// </summary>
        private class AsyncConsoleWriter : TextWriter
        {
            private readonly BlockingCollection<string> _queue;

            public AsyncConsoleWriter(BlockingCollection<string> queue)
            {
                _queue = queue;
            }

            public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

            public override void WriteLine(string value)
            {
                try
                {
                    if (!_queue.IsAddingCompleted)
                    {
                        string timestamp = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ";
                        _queue.TryAdd(timestamp + value + Environment.NewLine, millisecondsTimeout: 10);
                    }
                }
                catch (InvalidOperationException)
                {
                    // Queue is completed, ignore
                }
            }

            public override void Write(string value)
            {
                try
                {
                    if (!_queue.IsAddingCompleted)
                    {
                        _queue.TryAdd(value, millisecondsTimeout: 10);
                    }
                }
                catch (InvalidOperationException)
                {
                    // Queue is completed, ignore
                }
            }
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
            if (_currentLogLevel >= level)
            {
                if (_logWriter != null)
                {
                    // File mode: write to log file
                    _logWriter.WriteLineWithLevel(message, level);
                    _logWriter.WriteLine();
                }
                else
                {
                    // Console mode: write to console
                    string levelPrefix = level switch
                    {
                        LogLevel.Error => "[ERROR] ",
                        LogLevel.Warning => "[WARN] ",
                        LogLevel.Info => "[INFO] ",
                        LogLevel.Debug => "[DEBUG] ",
                        LogLevel.Trace => "[TRACE] ",
                        _ => ""
                    };
                    Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {levelPrefix}{message}");
                }
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
