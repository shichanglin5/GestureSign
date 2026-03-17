using System;
using System.IO;
using System.Text;

namespace GestureSign.ControlPanel.Common
{
    internal static class TrainingDiagnosticsSessionFileStore
    {
        private const string DiagnosticsDirectoryName = "GestureSign\\TrainingDiagnostics";

        public static string CreateSessionFilePath()
        {
            string directory = Path.Combine(Path.GetTempPath(), DiagnosticsDirectoryName);
            Directory.CreateDirectory(directory);

            return Path.Combine(
                directory,
                $"training-session-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");
        }

        public static string AppendRecording(string filePath, string diagnosticData)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                filePath = CreateSessionFilePath();

            Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? Path.GetTempPath());

            using (var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.WriteLine("[TrainingDiagnostics] Recording completed");
                writer.WriteLine(diagnosticData?.TrimEnd() ?? string.Empty);
                writer.WriteLine();
                writer.WriteLine();
            }

            return filePath;
        }
    }
}
