using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using GestureSign.Common.Gestures;
using GestureSign.Common.Input;
using GestureSign.Daemon.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GestureSign.Tests
{
    [TestClass]
    public class TrainingDiagnosticsCaseTests
    {
        [TestMethod]
        public void ReplayCaseFiles_MatchExpectedSequences()
        {
            int caseCount = 0;
            foreach (string caseFilePath in GetCaseFiles())
            {
                caseCount++;
                var replayCase = TrainingDiagnosticsReplayCase.Load(caseFilePath);
                var replayResult = TrainingDiagnosticsReplayRunner.Run(replayCase);

                if (replayCase.ExpectedSequence.SequenceEqual(replayResult.ActualSequence))
                    continue;

                Assert.Fail(replayResult.CreateFailureMessage());
            }

            if (caseCount == 0)
                Assert.Inconclusive("TestCases/TrainingDiagnostics/ 目录下没有找到任何 *.case.json 测试数据文件，测试未实际执行。");
        }

        public static IEnumerable<string> GetCaseFiles()
        {
            string casesRoot = TrainingDiagnosticsReplayPaths.GetCasesDirectory();
            if (!Directory.Exists(casesRoot))
                yield break;

            foreach (string caseFile in Directory.EnumerateFiles(casesRoot, "*.case.json", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                yield return caseFile;
            }
        }
    }

    internal sealed class TrainingDiagnosticsReplayCase
    {
        public string Name { get; set; }

        public string DiagnosticsFile { get; set; }

        public List<string> ExpectedSequence { get; set; } = new List<string>();

        public static TrainingDiagnosticsReplayCase Load(string caseFilePath)
        {
            string json = File.ReadAllText(caseFilePath);
            var replayCase = JsonSerializer.Deserialize<TrainingDiagnosticsReplayCase>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            Assert.IsNotNull(replayCase, $"Unable to deserialize replay case: {caseFilePath}");
            Assert.IsFalse(string.IsNullOrWhiteSpace(replayCase.DiagnosticsFile), $"Replay case is missing DiagnosticsFile: {caseFilePath}");

            if (!Path.IsPathRooted(replayCase.DiagnosticsFile))
            {
                replayCase.DiagnosticsFile = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(caseFilePath) ?? string.Empty, replayCase.DiagnosticsFile));
            }

            replayCase.Name ??= Path.GetFileNameWithoutExtension(caseFilePath);
            return replayCase;
        }
    }

    internal sealed class TrainingDiagnosticsReplayRunner
    {
        public static TrainingDiagnosticsReplayResult Run(TrainingDiagnosticsReplayCase replayCase)
        {
            Assert.IsTrue(File.Exists(replayCase.DiagnosticsFile), $"Diagnostics file was not found: {replayCase.DiagnosticsFile}");

            var diagnosticLines = File.ReadAllLines(replayCase.DiagnosticsFile);
            var blocks = TrainingDiagnosticsFileParser.Parse(diagnosticLines);
            var manager = ApplicationManager.Instance;
            manager.LoadingTask.Wait();

            var oldApps = manager.Applications;
            int oldMultiFingerDelay = AppConfig.MultiFingerDelay;
            int oldFixHoldMs = AppConfig.TipTapFixMinHoldMs;

            try
            {
                manager.RemoveAllApplication();
                var global = (GlobalApp)manager.GetGlobalApplication();
                bool hasPerBlockExpectations = blocks.Count == (replayCase.ExpectedSequence?.Count ?? 0);

                if (!hasPerBlockExpectations)
                {
                    ApplyReplayConfigs(global, BuildReplayTapConfigs(blocks, replayCase.ExpectedSequence), BuildReplayTipTapConfigs(blocks, replayCase.ExpectedSequence));
                }

                AppConfig.MultiFingerDelay = 0;
                AppConfig.TipTapFixMinHoldMs = 0;

                var actualSequence = new List<string>();

                foreach (var block in blocks)
                {
                    if (hasPerBlockExpectations)
                    {
                        string expected = replayCase.ExpectedSequence![actualSequence.Count];
                        ApplyReplayConfigs(global, BuildReplayTapConfigsForExpected(block, expected), BuildReplayTipTapConfigsForExpected(block, expected));
                    }

                    var capture = new PointCapture(Devices.TouchPad)
                    {
                        Mode = CaptureMode.Training
                    };

                    ReplayRawFrames(capture, block.RawFrameLines);
                    FinalizeBlockSession(capture, block);
                    actualSequence.AddRange(capture.PublishedTrainingDefinitionsForTest
                        .Select(FormatTrainingDefinition));
                }

                return new TrainingDiagnosticsReplayResult
                {
                    CaseName = replayCase.Name,
                    DiagnosticsFile = replayCase.DiagnosticsFile,
                    ExpectedSequence = replayCase.ExpectedSequence,
                    ActualSequence = actualSequence,
                    Segments = blocks.Select(block => new TrainingDiagnosticsReplaySegmentResult
                    {
                        StartLine = block.StartLine,
                        EndLine = block.EndLine,
                        RecordedResultType = block.ResultType,
                        RecordedResultName = block.ResultName,
                    }).ToList(),
                };
            }
            finally
            {
                AppConfig.MultiFingerDelay = oldMultiFingerDelay;
                AppConfig.TipTapFixMinHoldMs = oldFixHoldMs;
                manager.RemoveAllApplication();
                manager.AddApplicationRange(oldApps);
            }
        }

        private static void ApplyReplayConfigs(GlobalApp global, IEnumerable<TapGestureConfig> tapConfigs, IEnumerable<TipTapGestureConfig> tipTapConfigs)
        {
            global.ContactGestures.Taps.Clear();
            global.ContactGestures.TipTaps.Clear();

            foreach (var tapConfig in tapConfigs ?? Enumerable.Empty<TapGestureConfig>())
            {
                global.ContactGestures.Taps.Add(tapConfig);
            }

            foreach (var tipTapConfig in tipTapConfigs ?? Enumerable.Empty<TipTapGestureConfig>())
            {
                global.ContactGestures.TipTaps.Add(tipTapConfig);
            }
        }

        private static void ReplayRawFrames(PointCapture capture, IReadOnlyList<string> rawFrameLines)
        {
            foreach (string rawLine in rawFrameLines)
            {
                string line = rawLine.Trim();
                if (line.Length == 0)
                    continue;

                int eventStart = line.IndexOf("ms ", StringComparison.Ordinal);
                Assert.IsTrue(eventStart >= 0, $"Invalid raw frame line: {line}");

                eventStart += 3;
                int totalIndex = line.IndexOf(" total=", eventStart, StringComparison.Ordinal);
                Assert.IsTrue(totalIndex > eventStart, $"Invalid raw frame line: {line}");

                string eventKind = line.Substring(eventStart, totalIndex - eventStart).Trim();
                int totalFingerCount = ParseIntBetween(line, "total=", " count=");
                int pointsIndex = line.IndexOf("::", StringComparison.Ordinal);
                var points = pointsIndex >= 0
                    ? line.Substring(pointsIndex + 2)
                        .Split(new[] { " | " }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(ParseRawFramePoint)
                        .ToList()
                    : new List<InputPoint>();

                switch (eventKind)
                {
                    case "down":
                        capture.ProcessPointDownForTest(points, totalFingerCount);
                        break;
                    case "move":
                        capture.ProcessPointMoveForTest(points, totalFingerCount);
                        break;
                    case "up":
                        capture.ProcessPointUpForTest(points, totalFingerCount);
                        break;
                    default:
                        Assert.Fail($"Unsupported raw frame event kind: {eventKind}");
                        break;
                }
            }
        }

        private static void FinalizeBlockSession(PointCapture capture, TrainingDiagnosticsBlock block)
        {
            if (block == null || block.ActiveContactIds.Count == 0)
                return;

            var lastFrame = block.GetLastFramePoints();
            if (lastFrame.Count == 0)
                return;

            var syntheticUp = lastFrame
                .Where(point => block.ActiveContactIds.Contains(point.ContactIdentifier))
                .Select(point => new InputPoint(point.ContactIdentifier, point.Point, DeviceStates.None))
                .ToList();

            if (syntheticUp.Count == 0)
                return;

            capture.ProcessPointUpForTest(syntheticUp, syntheticUp.Count);
        }

        internal static InputPoint ParseRawFramePointForTest(string pointSpec)
        {
            return ParseRawFramePoint(pointSpec);
        }

        private static InputPoint ParseRawFramePoint(string pointSpec)
        {
            int contactIdentifier = ParseIntBetween(pointSpec, "id=", " state=");
            string stateToken = ParseStringBetween(pointSpec, "state=", " raw=");
            string raw = ParseStringBetween(pointSpec, "raw=(", ")");
            string[] coordinates = raw.Split(',');

            return new InputPoint(
                contactIdentifier,
                new Point(
                    int.Parse(coordinates[0], CultureInfo.InvariantCulture),
                    int.Parse(coordinates[1], CultureInfo.InvariantCulture)),
                string.Equals(stateToken, "None", StringComparison.OrdinalIgnoreCase)
                    ? DeviceStates.None
                    : DeviceStates.Tip);
        }

        private static int ParseIntBetween(string text, string startToken, string endToken)
        {
            return int.Parse(ParseStringBetween(text, startToken, endToken), CultureInfo.InvariantCulture);
        }

        private static string ParseStringBetween(string text, string startToken, string endToken)
        {
            int startIndex = text.IndexOf(startToken, StringComparison.Ordinal);
            Assert.IsTrue(startIndex >= 0, $"Token '{startToken}' was not found in '{text}'");
            startIndex += startToken.Length;

            int endIndex = text.IndexOf(endToken, startIndex, StringComparison.Ordinal);
            Assert.IsTrue(endIndex >= startIndex, $"Token '{endToken}' was not found in '{text}'");
            return text.Substring(startIndex, endIndex - startIndex);
        }

        private static string FormatActualSequence(IReadOnlyList<RecordedGestureDefinitionResult> definitions)
        {
            if (definitions == null || definitions.Count == 0)
                return "None";

            if (definitions.Count == 1)
                return FormatTrainingDefinition(definitions[0]);

            return string.Join(" -> ", definitions.Select(FormatTrainingDefinition));
        }

        private static string FormatTrainingDefinition(RecordedGestureDefinitionResult definition)
        {
            switch (definition.Type)
            {
                case RecordedGestureType.TipTap:
                    return $"TipTap:{definition.TipTapGesture?.Direction}";
                case RecordedGestureType.Tap:
                    return $"Tap:{definition.FingerCount}";
                default:
                    return definition.Type.ToString();
            }
        }

        private static IEnumerable<TipTapGestureConfig> BuildReplayTipTapConfigs(
            IReadOnlyList<TrainingDiagnosticsBlock> blocks,
            IReadOnlyList<string> expectedSequence)
        {
            var configs = blocks
                .Select(block => block.TryCreateTipTapConfig())
                .OfType<TipTapGestureConfig>()
                .ToList();

            var defaultTemplate = configs.FirstOrDefault()
                ?? blocks.FirstOrDefault(block => block.FingerCount >= 2 && block.TipTapFixFingerCount >= 1)?.CreateFallbackTipTapTemplate();

            if (defaultTemplate != null)
            {
                foreach (string expected in expectedSequence ?? Array.Empty<string>())
                {
                    if (!TryParseExpectedTipTapDirection(expected, out var direction))
                        continue;

                    bool alreadyPresent = configs.Any(config =>
                        config.FingerCount == defaultTemplate.FingerCount &&
                        config.FixFingerCount == defaultTemplate.FixFingerCount &&
                        config.Direction == direction);
                    if (alreadyPresent)
                        continue;

                    configs.Add(new TipTapGestureConfig
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Name = $"TipTap {direction} ({defaultTemplate.FixFingerCount} fixed)",
                        FingerCount = defaultTemplate.FingerCount,
                        FixFingerCount = defaultTemplate.FixFingerCount,
                        Direction = direction,
                        Recognition = new TipTapRecognition
                        {
                            MaxTapDurationMs = defaultTemplate.Recognition?.MaxTapDurationMs ?? AppConfig.TipTapMaxTapDurationMs,
                            FixStillThresholdPx = defaultTemplate.Recognition?.FixStillThresholdPx ?? 12,
                            TapMaxMovementPx = defaultTemplate.Recognition?.TapMaxMovementPx ?? 30,
                            DirectionDeadzonePx = defaultTemplate.Recognition?.DirectionDeadzonePx ?? 24,
                            RepeatCooldownMs = 0,
                        }
                    });
                }
            }

            return configs
                .GroupBy(config => $"{config.FingerCount}:{config.FixFingerCount}:{config.Direction}")
                .Select(group => group.First())
                .ToList();
        }

        private static IEnumerable<TapGestureConfig> BuildReplayTapConfigs(
            IReadOnlyList<TrainingDiagnosticsBlock> blocks,
            IReadOnlyList<string> expectedSequence)
        {
            var configs = blocks
                .Select(block => block.TryCreateTapConfig())
                .OfType<TapGestureConfig>()
                .ToList();

            foreach (string expected in expectedSequence ?? Array.Empty<string>())
            {
                if (!TryParseExpectedTapFingerCount(expected, out int fingerCount))
                    continue;

                if (configs.Any(config => config.FingerCount == fingerCount))
                    continue;

                configs.Add(new TapGestureConfig
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = $"{fingerCount}指轻点",
                    FingerCount = fingerCount,
                    Recognition = new TapGestureRecognition
                    {
                        MaxDurationMs = 300,
                        MaxMovementPx = 18,
                        MinFingerCount = fingerCount,
                    }
                });
            }

            return configs
                .GroupBy(config => config.FingerCount)
                .Select(group => group.First())
                .ToList();
        }

        private static IEnumerable<TapGestureConfig> BuildReplayTapConfigsForExpected(TrainingDiagnosticsBlock block, string expected)
        {
            if (TryParseExpectedTapFingerCount(expected, out int fingerCount))
            {
                return new[]
                {
                    block.TryCreateTapConfig() is TapGestureConfig recordedTap && recordedTap.FingerCount == fingerCount
                        ? recordedTap
                        : new TapGestureConfig
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            Name = $"{fingerCount}指轻点",
                            FingerCount = fingerCount,
                            Recognition = new TapGestureRecognition
                            {
                                MaxDurationMs = block.TapMaxDurationMs,
                                MaxMovementPx = block.TapMaxMovementPx,
                                MinFingerCount = fingerCount,
                            }
                        }
                };
            }

            return Enumerable.Empty<TapGestureConfig>();
        }

        private static IEnumerable<TipTapGestureConfig> BuildReplayTipTapConfigsForExpected(TrainingDiagnosticsBlock block, string expected)
        {
            if (TryParseExpectedTipTapDirection(expected, out var direction))
            {
                var template = block.TryCreateTipTapConfig() ?? block.CreateFallbackTipTapTemplate();
                template.Direction = direction;
                template.Recognition.RepeatCooldownMs = 0;
                template.Id = Guid.NewGuid().ToString("N");
                return new[] { template };
            }

            return Enumerable.Empty<TipTapGestureConfig>();
        }

        private static bool TryParseExpectedTapFingerCount(string expected, out int fingerCount)
        {
            fingerCount = 0;
            const string prefix = "Tap:";
            if (string.IsNullOrWhiteSpace(expected) || !expected.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            return int.TryParse(expected.Substring(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out fingerCount);
        }

        private static bool TryParseExpectedTipTapDirection(string expected, out ContactGestureDirection direction)
        {
            direction = ContactGestureDirection.None;
            const string prefix = "TipTap:";
            if (string.IsNullOrWhiteSpace(expected) || !expected.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            return Enum.TryParse(expected.Substring(prefix.Length), ignoreCase: true, out direction);
        }
    }

    internal sealed class TrainingDiagnosticsReplayResult
    {
        public string CaseName { get; set; }

        public string DiagnosticsFile { get; set; }

        public IReadOnlyList<string> ExpectedSequence { get; set; }

        public IReadOnlyList<string> ActualSequence { get; set; } = Array.Empty<string>();

        public IReadOnlyList<TrainingDiagnosticsReplaySegmentResult> Segments { get; set; }

        public string CreateFailureMessage()
        {
            var lines = new List<string>
            {
                $"Replay case failed: {CaseName}",
                $"Diagnostics file: {DiagnosticsFile}",
                $"Expected: [{string.Join(", ", ExpectedSequence ?? Array.Empty<string>())}]",
                $"Actual:   [{string.Join(", ", ActualSequence)}]",
                string.Empty,
            };

            int maxCount = Math.Max(ExpectedSequence?.Count ?? 0, ActualSequence?.Count ?? 0);
            for (int index = 0; index < maxCount; index++)
            {
                string expected = index < (ExpectedSequence?.Count ?? 0) ? ExpectedSequence[index] : "<missing>";
                string actual = index < (ActualSequence?.Count ?? 0) ? ActualSequence[index] : "<missing>";
                string status = string.Equals(expected, actual, StringComparison.Ordinal) ? "PASS" : "FAIL";
                lines.Add($"Gesture {index + 1}: expected={expected}, actual={actual}, {status}");
            }

            if (Segments != null && Segments.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("Recorded blocks:");
                for (int index = 0; index < Segments.Count; index++)
                {
                    var segment = Segments[index];
                    lines.Add($"Block {index + 1}: recorded={segment.RecordedResultType}:{segment.RecordedResultName}, lines={segment.StartLine}-{segment.EndLine}");
                }
            }

            return string.Join(Environment.NewLine, lines);
        }
    }

    internal sealed class TrainingDiagnosticsReplaySegmentResult
    {
        public int StartLine { get; set; }

        public int EndLine { get; set; }

        public string RecordedResultType { get; set; }

        public string RecordedResultName { get; set; }
    }

    internal static class TrainingDiagnosticsReplayPaths
    {
        public static string GetCasesDirectory()
        {
            string? directory = new DirectoryInfo(AppContext.BaseDirectory).FullName;
            while (!string.IsNullOrEmpty(directory))
            {
                string projectFile = Path.Combine(directory, "GestureSign.Tests.csproj");
                if (File.Exists(projectFile))
                    return Path.Combine(directory, "TestCases", "TrainingDiagnostics");

                directory = Directory.GetParent(directory)?.FullName;
            }

            throw new DirectoryNotFoundException("Unable to locate GestureSign.Tests project directory.");
        }
    }

    internal static class TrainingDiagnosticsFileParser
    {
        public static List<TrainingDiagnosticsBlock> Parse(IReadOnlyList<string> lines)
        {
            var blocks = new List<TrainingDiagnosticsBlock>();
            TrainingDiagnosticsBlock? currentBlock = null;

            for (int index = 0; index < lines.Count; index++)
            {
                string line = lines[index];
                if (string.Equals(line.Trim(), "GestureSign Recording Diagnostics", StringComparison.Ordinal))
                {
                    if (currentBlock != null)
                    {
                        currentBlock.EndLine = index;
                        blocks.Add(currentBlock);
                    }

                    currentBlock = new TrainingDiagnosticsBlock
                    {
                        StartLine = index + 1,
                    };
                }

                currentBlock?.Lines.Add(line);
            }

            if (currentBlock != null)
            {
                currentBlock.EndLine = lines.Count;
                blocks.Add(currentBlock);
            }

            foreach (var block in blocks)
            {
                block.Parse();
            }

            return blocks;
        }
    }

    internal sealed class TrainingDiagnosticsBlock
    {
        public int StartLine { get; set; }

        public int EndLine { get; set; }

        public string ResultType { get; private set; } = string.Empty;

        public string ResultName { get; private set; } = string.Empty;

        public List<string> Lines { get; } = new List<string>();

        public List<string> RawFrameLines { get; } = new List<string>();

        public int FingerCount { get; private set; }

        public List<int> ActiveContactIds { get; } = new List<int>();

        public int TipTapFixFingerCount { get; private set; }

        public ContactGestureDirection TipTapDirection { get; private set; }

        public int TipTapMaxTapDurationMs { get; private set; } = AppConfig.TipTapMaxTapDurationMs;

        public int TipTapFixStillThresholdPx { get; private set; } = 12;

        public int TipTapTapMaxMovementPx { get; private set; } = 30;

        public int TipTapDirectionDeadzonePx { get; private set; } = 24;

        public int TapMaxDurationMs { get; private set; } = 220;

        public double TapMaxMovementPx { get; private set; } = 18;

        public void Parse()
        {
            bool inRawFrames = false;
            foreach (string line in Lines)
            {
                if (string.Equals(line.Trim(), "[RawFrames]", StringComparison.Ordinal))
                {
                    inRawFrames = true;
                    continue;
                }

                if (inRawFrames)
                {
                    if (line.StartsWith("[", StringComparison.Ordinal))
                    {
                        inRawFrames = false;
                    }
                    else if (!string.IsNullOrWhiteSpace(line) && !string.Equals(line.Trim(), "none", StringComparison.OrdinalIgnoreCase))
                    {
                        RawFrameLines.Add(line);
                        continue;
                    }
                }

                TryParseHeader(line);
            }
        }

        public TipTapGestureConfig? TryCreateTipTapConfig()
        {
            if (!string.Equals(ResultType, nameof(RecordedGestureType.TipTap), StringComparison.OrdinalIgnoreCase))
                return null;

            return CreateFallbackTipTapTemplate();
        }

        public TapGestureConfig? TryCreateTapConfig()
        {
            if (!string.Equals(ResultType, nameof(RecordedGestureType.Tap), StringComparison.OrdinalIgnoreCase) || FingerCount <= 0)
                return null;

            return new TapGestureConfig
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = string.IsNullOrWhiteSpace(ResultName) ? $"{FingerCount}指轻点" : ResultName,
                FingerCount = FingerCount,
                Recognition = new TapGestureRecognition
                {
                    MaxDurationMs = TapMaxDurationMs,
                    MaxMovementPx = TapMaxMovementPx,
                    MinFingerCount = FingerCount,
                }
            };
        }

        public TipTapGestureConfig CreateFallbackTipTapTemplate()
        {
            return new TipTapGestureConfig
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = string.IsNullOrWhiteSpace(ResultName) ? $"TipTap {TipTapDirection} ({TipTapFixFingerCount} fixed)" : ResultName,
                FingerCount = FingerCount,
                FixFingerCount = TipTapFixFingerCount,
                Direction = TipTapDirection,
                Recognition = new TipTapRecognition
                {
                    MaxTapDurationMs = TipTapMaxTapDurationMs,
                    FixStillThresholdPx = TipTapFixStillThresholdPx,
                    TapMaxMovementPx = TipTapTapMaxMovementPx,
                    DirectionDeadzonePx = TipTapDirectionDeadzonePx,
                    RepeatCooldownMs = 0,
                }
            };
        }

        public List<InputPoint> GetLastFramePoints()
        {
            if (RawFrameLines.Count == 0)
                return new List<InputPoint>();

            string line = RawFrameLines[RawFrameLines.Count - 1].Trim();
            int pointsIndex = line.IndexOf("::", StringComparison.Ordinal);
            if (pointsIndex < 0)
                return new List<InputPoint>();

            return line.Substring(pointsIndex + 2)
                .Split(new[] { " | " }, StringSplitOptions.RemoveEmptyEntries)
                .Select(TrainingDiagnosticsReplayRunner.ParseRawFramePointForTest)
                .ToList();
        }

        private void TryParseHeader(string line)
        {
            if (TryParseValue(line, "ResultType", out string resultType))
            {
                ResultType = resultType;
                return;
            }

            if (TryParseValue(line, "ResultName", out string resultName))
            {
                ResultName = resultName;
                return;
            }

            if (TryParseValue(line, "FingerCount", out string fingerCount) && int.TryParse(fingerCount, out int parsedFingerCount))
            {
                FingerCount = parsedFingerCount;
                return;
            }

            if (TryParseValue(line, "ActiveContactIds", out string activeContactIds))
            {
                ActiveContactIds.Clear();
                ActiveContactIds.AddRange(ParseIntList(activeContactIds));
                return;
            }

            if (TryParseValue(line, "TipTap.FixFingerCount", out string fixFingerCount) && int.TryParse(fixFingerCount, out int parsedFixFingerCount))
            {
                TipTapFixFingerCount = parsedFixFingerCount;
                return;
            }

            if (TryParseValue(line, "TipTap.Direction", out string direction) && Enum.TryParse(direction, true, out ContactGestureDirection parsedDirection))
            {
                TipTapDirection = parsedDirection;
                return;
            }

            if (TryParseValue(line, "TipTap.MaxTapDurationMs", out string maxTapDuration) && int.TryParse(maxTapDuration, out int parsedMaxTapDuration))
            {
                TipTapMaxTapDurationMs = parsedMaxTapDuration;
                return;
            }

            if (TryParseValue(line, "TipTap.FixStillThresholdPx", out string fixStillThreshold) && int.TryParse(fixStillThreshold, out int parsedFixStillThreshold))
            {
                TipTapFixStillThresholdPx = parsedFixStillThreshold;
                return;
            }

            if (TryParseValue(line, "TipTap.TapMaxMovementPx", out string tapMaxMovement) && int.TryParse(tapMaxMovement, out int parsedTapMaxMovement))
            {
                TipTapTapMaxMovementPx = parsedTapMaxMovement;
                return;
            }

            if (TryParseValue(line, "TipTap.DirectionDeadzonePx", out string deadzone) && int.TryParse(deadzone, out int parsedDeadzone))
            {
                TipTapDirectionDeadzonePx = parsedDeadzone;
                return;
            }

            if (TryParseValue(line, "Tap.MaxDurationMs", out string tapMaxDuration) && int.TryParse(tapMaxDuration, out int parsedTapMaxDuration))
            {
                TapMaxDurationMs = parsedTapMaxDuration;
                return;
            }

            if (TryParseValue(line, "Tap.MaxMovementPx", out string tapMovement) && double.TryParse(tapMovement, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedTapMovement))
            {
                TapMaxMovementPx = parsedTapMovement;
            }
        }

        private static bool TryParseValue(string line, string key, out string value)
        {
            string prefix = key + ":";
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                value = line.Substring(prefix.Length).Trim();
                return true;
            }

            value = string.Empty;
            return false;
        }

        private static IEnumerable<int> ParseIntList(string value)
        {
            string trimmed = value.Trim();
            if (trimmed.Length < 2 || trimmed[0] != '[' || trimmed[trimmed.Length - 1] != ']')
                return Array.Empty<int>();

            string content = trimmed.Substring(1, trimmed.Length - 2);
            if (string.IsNullOrWhiteSpace(content))
                return Array.Empty<int>();

            return content.Split(',')
                .Select(item => int.Parse(item.Trim(), CultureInfo.InvariantCulture))
                .ToList();
        }
    }
}
