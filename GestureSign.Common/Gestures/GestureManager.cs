using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using GestureSign.PointPatterns;
using GestureSign.Common.Input;
using GestureSign.Common.Log;
using Newtonsoft.Json;

namespace GestureSign.Common.Gestures
{
    public class GestureManager : IGestureManager
    {
        #region Private Variables

        // Create thread-safe lazy singleton instance
        private static readonly Lazy<GestureManager> _instance = new Lazy<GestureManager>(() => new GestureManager());

        // Create read/write list of IGestures to hold system gestures
        private List<IGesture> _Gestures;

        // Create PointPatternAnalyzer to process gestures when received
        PointPatternAnalyzer gestureAnalyzer = null;

        private string _matchedGestureId;
        private GestureIndexSnapshot _gestureIndexSnapshot;
        private bool _gestureIdsRepairedOnLoad;
        private readonly object _gesturesLock = new object();

        private sealed class GestureIndexSnapshot
        {
            public Dictionary<(int FingerCount, int TrajectoryCount), List<IGesture>> GestureIndex { get; init; }
            public Dictionary<(int FingerCount, int TrajectoryCount, int TrajectoryIndex), PointsPatternSet[]> PatternSetCache { get; init; }
        }

        private sealed class GestureIdRepair
        {
            public string OldId { get; init; }
            public string NewId { get; init; }
            public string GestureName { get; init; }
        }

        #endregion

        #region Public Instance Properties

        public IGesture[] Gestures
        {
            get
            {
                lock (_gesturesLock)
                {
                    _Gestures ??= new List<IGesture>();
                    return _Gestures.ToArray();
                }
            }
        }

        public Task LoadingTask { get; }

        #endregion

        #region Constructors

        protected GestureManager()
        {
            LoadingTask = LoadGestures();
            // Instantiate gesture analyzer using gestures loaded from file
            gestureAnalyzer = new PointPatternAnalyzer();//Gestures
            // Set tap threshold from configuration
            gestureAnalyzer.TapThreshold = Configuration.AppConfig.TapDistanceThreshold;
        }

        private void SetGesturesSnapshot(List<IGesture> gestures)
        {
            lock (_gesturesLock)
            {
                var snapshot = gestures ?? new List<IGesture>();
                _Gestures = snapshot;
                RebuildGestureIndex(snapshot);
            }
        }

        private List<IGesture> GetGesturesListSnapshot()
        {
            lock (_gesturesLock)
            {
                _Gestures ??= new List<IGesture>();
                return new List<IGesture>(_Gestures);
            }
        }

        #endregion

        #region Public Type Properties

        public static GestureManager Instance
        {
            get { return _instance.Value; }
        }

        #endregion

        #region Events

        #endregion

        #region Custom Events

        public static event EventHandler OnLoadGesturesCompleted;
        public static event EventHandler GestureSaved;

        #endregion

        #region Private Methods

        private bool LoadDefaults()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Defaults", Constants.GesturesFileName);
            var gestures = LoadGesturesFromFile(path);
            SetGesturesSnapshot(gestures);

            return gestures != null;
        }

        private bool LoadBackup()
        {
            var directory = new DirectoryInfo(AppConfig.BackupPath);
            if (directory.Exists)
            {
                var files = directory.EnumerateFiles("*" + Constants.GesturesExtension).OrderByDescending(f => f.LastWriteTime);
                foreach (var file in files)
                {
                    var gestures = LoadGesturesFromFile(file.FullName);
                    if (gestures != null)
                    {
                        SetGesturesSnapshot(gestures);
                        return true;
                    }
                }
            }
            return false;
        }

        private static string GetRandomString(Random random, int length)
        {
            string input = "abcdefghijklmnopqrstuvwxyz0123456789";
            var chars = Enumerable.Range(0, length).Select(x => input[random.Next(0, input.Length)]);
            return new string(chars.ToArray());
        }

        #endregion

        #region Public Methods

        public void Load(IPointCapture pointCapture)
        {
            // Shortcut method to control singleton instantiation

            // GestureManager no longer needs to subscribe BeforePointsCaptured;
            // recognition is now triggered explicitly via Recognize() from PointCapture.
        }

        private void RebuildGestureIndex(IReadOnlyList<IGesture> gestures)
        {
            var gestureIndex = new Dictionary<(int FingerCount, int TrajectoryCount), List<IGesture>>();
            var patternSetCache = new Dictionary<(int FingerCount, int TrajectoryCount, int TrajectoryIndex), PointsPatternSet[]>();

            foreach (var gesture in gestures)
            {
                if (gesture?.PointPatterns == null || gesture.PointPatterns.Length == 0)
                    continue;

                var pattern = gesture.PointPatterns[0];
                int trajectoryCount = pattern?.Points?.Length ?? 0;
                if (trajectoryCount == 0)
                    continue;

                var key = (gesture.FingerCount, trajectoryCount);
                if (!gestureIndex.TryGetValue(key, out var list))
                {
                    list = new List<IGesture>();
                    gestureIndex[key] = list;
                }

                list.Add(gesture);
            }

            foreach (var kvp in gestureIndex)
            {
                var key = kvp.Key;
                var indexedGestures = kvp.Value;
                for (int trajectoryIndex = 0; trajectoryIndex < key.TrajectoryCount; trajectoryIndex++)
                {
                    var patternSet = new PointsPatternSet[indexedGestures.Count];
                    for (int gestureArrayIndex = 0; gestureArrayIndex < indexedGestures.Count; gestureArrayIndex++)
                    {
                        patternSet[gestureArrayIndex] = new PointsPatternSet(
                            indexedGestures[gestureArrayIndex].Name,
                            indexedGestures[gestureArrayIndex].PointPatterns[0].Points[trajectoryIndex]);
                    }

                    patternSetCache[(key.FingerCount, key.TrajectoryCount, trajectoryIndex)] = patternSet;
                }
            }

            _gestureIndexSnapshot = new GestureIndexSnapshot
            {
                GestureIndex = gestureIndex,
                PatternSetCache = patternSetCache,
            };
        }

        public void AddGesture(IGesture Gesture)
        {
            var snapshot = Gestures.ToList();
            snapshot.Add(Gesture);
            SetGesturesSnapshot(snapshot);
        }

        public Task LoadGestures()
        {
            var repairedBindings = new List<GestureIdRepair>();

            Action<bool> loadCompleted =
                   result =>
                   {
                       if (!result)
                           if (!LoadBackup())
                                if (!LoadDefaults())
                                    SetGesturesSnapshot(new List<IGesture>());

                       if (_gestureIdsRepairedOnLoad)
                       {
                           RepairApplicationGestureBindings(repairedBindings);
                           SaveGestures();
                           _gestureIdsRepairedOnLoad = false;
                       }
                       OnLoadGesturesCompleted?.Invoke(this, EventArgs.Empty);
                   };

            var startLoading = Task.Run(() =>
            {
                try
                {
                    string path = Path.Combine(AppConfig.ApplicationDataPath, Constants.GesturesFileName);
                    var gestures = LoadGesturesFromFile(path);

                    if (gestures != null)
                    {
                        if (gestures.Count == 0 || gestures[0].PointPatterns == null)
                        {
                            List<LegacyGesture> legacyGestures = FileManager.LoadObject<List<LegacyGesture>>(path, true);

                            foreach (var gesture in legacyGestures)
                            {
                                if (gesture.Points != null)
                                    gesture.PointPatterns = new[] { new PointPattern(gesture.Points) };
                            }
                            var normalizedLegacy = legacyGestures.Cast<IGesture>().ToList();
                            var repairedLegacy = NormalizeGestureIds(normalizedLegacy);
                            repairedBindings.AddRange(repairedLegacy);
                            _gestureIdsRepairedOnLoad |= repairedLegacy.Count > 0;
                            SetGesturesSnapshot(normalizedLegacy);
                        }
                        else
                        {
                            var repaired = NormalizeGestureIds(gestures);
                            repairedBindings.AddRange(repaired);
                            _gestureIdsRepairedOnLoad |= repaired.Count > 0;
                            SetGesturesSnapshot(gestures);
                        }
                    }


                    return gestures != null;
                }
                catch
                {
                    return false;
                }
            });

            return startLoading.ContinueWith(antecendent => loadCompleted(antecendent.Result));
        }

        private List<GestureIdRepair> NormalizeGestureIds(List<IGesture> gestures)
        {
            var repairs = new List<GestureIdRepair>();
            if (gestures == null || gestures.Count == 0)
                return repairs;

            var usedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var gesture in gestures)
            {
                if (gesture == null)
                    continue;

                bool needsRepair = string.IsNullOrWhiteSpace(gesture.Id) || usedIds.Contains(gesture.Id);
                if (needsRepair)
                {
                    string oldId = gesture.Id;
                    gesture.Id = GetNewGestureId(gesture.PointPatterns);
                    while (usedIds.Contains(gesture.Id))
                    {
                        gesture.Id += "_1";
                    }

                    repairs.Add(new GestureIdRepair
                    {
                        OldId = oldId,
                        NewId = gesture.Id,
                        GestureName = gesture.Name,
                    });
                    Log.Logging.LogWarning($"[LoadGesturesFromFile] Duplicate/empty gesture id repaired: {oldId} -> {gesture.Id} ({gesture.Name})");
                }

                usedIds.Add(gesture.Id);
            }

            return repairs;
        }

        private void RepairApplicationGestureBindings(IReadOnlyCollection<GestureIdRepair> repairs)
        {
            if (repairs == null || repairs.Count == 0)
                return;

            try
            {
                var applicationManager = ApplicationManager.Instance;
                ExecuteAfterTaskCompletion(applicationManager.LoadingTask, () => RepairApplicationGestureBindingsCore(applicationManager, repairs));
            }
            catch (Exception ex)
            {
                Log.Logging.LogException(ex);
            }
        }

        private static void ExecuteAfterTaskCompletion(Task dependency, System.Action action)
        {
            if (action == null)
                return;

            if (dependency == null || dependency.IsCompleted)
            {
                action();
                return;
            }

            dependency.ContinueWith(
                _ => action(),
                TaskScheduler.Default);
        }

        private static void RepairApplicationGestureBindingsCore(ApplicationManager applicationManager, IReadOnlyCollection<GestureIdRepair> repairs)
        {
            int repairedActionCount = 0;
            foreach (var app in applicationManager.Applications)
            {
                if (app?.Actions == null)
                    continue;

                foreach (var action in app.Actions)
                {
                    if (action == null)
                        continue;

                    var repair = repairs.FirstOrDefault(r =>
                        !string.IsNullOrWhiteSpace(r.NewId) &&
                        ((!string.IsNullOrWhiteSpace(r.OldId) &&
                          string.Equals(action.GestureId, r.OldId, StringComparison.Ordinal) &&
                          string.Equals(action.GestureName, r.GestureName, StringComparison.Ordinal)) ||
                         (string.IsNullOrWhiteSpace(action.GestureId) &&
                          string.Equals(action.GestureName, r.GestureName, StringComparison.Ordinal))));

                    if (repair == null || string.Equals(action.GestureId, repair.NewId, StringComparison.Ordinal))
                        continue;

                    action.GestureId = repair.NewId;
                    action.GestureName = repair.GestureName;
                    repairedActionCount++;
                }
            }

            if (repairedActionCount > 0)
            {
                applicationManager.SaveApplications();
                Log.Logging.LogWarning($"[LoadGestures] Rebound {repairedActionCount} action gesture binding(s) after repairing gesture ids.");
            }
        }

        public bool SaveGestures()
        {
            try
            {
                var snapshot = Gestures;

                bool flag = Configuration.FileManager.SaveObject(snapshot, Path.Combine(AppConfig.ApplicationDataPath, Constants.GesturesFileName));

                if (flag)
                {
                    GestureSaved?.Invoke(this, EventArgs.Empty);
                }
                return flag;
            }
            catch (Exception ex)
            {
                Log.Logging.LogException(ex);
                return false;
            }
        }

        public static List<IGesture> LoadGesturesFromFile(string filePath, bool throwException = false)
        {

            if (!File.Exists(filePath))
            {
                return null;
            }

            List<IGesture> gestureList = new List<IGesture>();

            try
            {
                string json;
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs))
                {
                    json = sr.ReadToEnd();
                }

                JsonTextReader reader = new JsonTextReader(new StringReader(json));

                while (reader.Read())
                {
                    if (reader.TokenType == JsonToken.StartObject)
                    {
                        Gesture gesture = new Gesture();
                        List<PointPattern> pointPatternList = new List<PointPattern>();
                        string gestureId = null;
                        string gestureName = null;
                        int fingerCount = 0;
                        var matchStrategy = FingerMatchStrategy.Inherit;
                        var modifiers = GestureModifiers.Default;

                        while (reader.Read())
                        {
                            if (reader.TokenType == JsonToken.EndObject)
                            {
                                // Update PointPatterns with FingerCount
                                foreach (var pp in pointPatternList)
                                {
                                    pp.FingerCount = fingerCount;
                                }

                                gesture.Id = string.IsNullOrWhiteSpace(gestureId)
                                    ? GetDeterministicGestureId(gestureName, pointPatternList.ToArray(), fingerCount)
                                    : gestureId;
                                gesture.Name = gestureName;
                                gesture.FingerCount = fingerCount;
                                gesture.MatchStrategy = matchStrategy;
                                gesture.Modifiers = modifiers;
                                gesture.PointPatterns = pointPatternList.ToArray();

                                // Only add gesture if it has valid patterns
                                if (gesture.Name != null && gesture.PointPatterns != null && gesture.PointPatterns.Length > 0)
                                {
                                    if (gestureList.Any(g => g.Id == gesture.Id))
                                    {
                                        Log.Logging.LogWarning($"[LoadGesturesFromFile] Duplicate gesture id detected: {gesture.Id} ({gesture.Name})");
                                    }
                                    gestureList.Add(gesture);
                                }
                                break;
                            }

                            if (reader.TokenType != JsonToken.PropertyName) continue;

                            string propertyName = (string)reader.Value;
                            if (propertyName == "Id")
                            {
                                gestureId = reader.ReadAsString();
                            }
                            else if (propertyName == "Name")
                            {
                                gestureName = reader.ReadAsString();
                            }
                            else if (propertyName == "FingerCount")
                            {
                                fingerCount = reader.ReadAsInt32() ?? 0;
                            }
                            else if (propertyName == "MatchStrategy")
                            {
                                int rawStrategy = reader.ReadAsInt32() ?? (int)FingerMatchStrategy.Inherit;
                                matchStrategy = Enum.IsDefined(typeof(FingerMatchStrategy), rawStrategy)
                                    ? (FingerMatchStrategy)rawStrategy
                                    : FingerMatchStrategy.Inherit;
                            }
                            else if (propertyName == "Modifiers")
                            {
                                int rawModifiers = reader.ReadAsInt32() ?? (int)GestureModifiers.Default;
                                modifiers = (GestureModifiers)rawModifiers;
                            }
                            else if (propertyName == "PointPatterns")
                            {
                                // Read PointPatterns array
                                if (!reader.Read() || reader.TokenType != JsonToken.StartArray) continue;

                                while (reader.Read() && reader.TokenType != JsonToken.EndArray)
                                {
                                    if (reader.TokenType == JsonToken.StartObject)
                                    {
                                        // Read PointPattern object
                                        List<Point[]> strokeList = null;

                                        while (reader.Read() && reader.TokenType != JsonToken.EndObject)
                                        {
                                            if (reader.TokenType == JsonToken.PropertyName && (string)reader.Value == "Points")
                                            {
                                                // Read Points array
                                                if (!reader.Read() || reader.TokenType != JsonToken.StartArray) continue;

                                                strokeList = new List<Point[]>();
                                                while (reader.Read() && reader.TokenType != JsonToken.EndArray)
                                                {
                                                    if (reader.TokenType == JsonToken.StartArray)
                                                    {
                                                        // Read stroke (array of point strings)
                                                        var stroke = new List<Point>();
                                                        while (reader.Read() && reader.TokenType != JsonToken.EndArray)
                                                        {
                                                            if (reader.TokenType == JsonToken.String)
                                                            {
                                                                var parts = ((string)reader.Value).Split(',');
                                                                if (parts.Length == 2)
                                                                {
                                                                    stroke.Add(new Point(
                                                                        Convert.ToInt32(parts[0].Trim()),
                                                                        Convert.ToInt32(parts[1].Trim())));
                                                                }
                                                            }
                                                        }
                                                        if (stroke.Count > 0)
                                                        {
                                                            strokeList.Add(stroke.ToArray());
                                                        }
                                                    }
                                                }
                                            }
                                        }

                                        if (strokeList != null && strokeList.Count > 0)
                                        {
                                            pointPatternList.Add(new PointPattern(strokeList.ToArray(), 0));
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Logging.LogException(e);
                if (throwException)
                    throw;
                return null;
            }


            return gestureList;
        }

        public GestureMatchResult? Recognize(Point[][] points, int fingerCount, GestureModifiers activeModifiers = GestureModifiers.Default)
        {
            var name = GetGestureSetNameMatch(points, fingerCount, GetGesturesListSnapshot(), activeModifiers);
            if (name == null) return null;
            return new GestureMatchResult(name, _matchedGestureId);
        }

        public string GetGestureSetNameMatch(Point[][] points, int fingerCount, List<IGesture> sourceGestures, GestureModifiers activeModifiers = GestureModifiers.Default)
        {
            if (points.Length == 0 || sourceGestures == null || sourceGestures.Count == 0)
            {
                _matchedGestureId = null;
                return null;
            }

            int trajectoryCount = points.Length;
            int lookupFingerCount = GetLookupFingerCount(fingerCount, trajectoryCount);
            List<IGesture> gestures;

            var indexSnapshot = _gestureIndexSnapshot;
            if (indexSnapshot?.GestureIndex != null && indexSnapshot.GestureIndex.TryGetValue((lookupFingerCount, trajectoryCount), out var indexedGestures))
            {
                gestures = new List<IGesture>(indexedGestures.Count);
                gestures.AddRange(indexedGestures);
            }
            else
            {
                gestures = new List<IGesture>(sourceGestures.Count);

                for (int i = 0; i < sourceGestures.Count; i++)
                {
                    IGesture g = sourceGestures[i];
                    if (g.PointPatterns != null &&
                        g.PointPatterns.Length > 0 &&
                        g.PointPatterns[0].Points != null &&
                        g.PointPatterns[0].Points.Length == trajectoryCount &&
                        g.FingerCount == lookupFingerCount)
                    {
                        gestures.Add(g);
                    }
                }
            }

            gestures = gestures.Where(g => g.Modifiers == activeModifiers).ToList();

            if (gestures.Count == 0)
            {
                _matchedGestureId = null;
                Logging.LogTrace($"[GestureMatch] No gesture registered for fingerCount={lookupFingerCount}, trajectoryCount={trajectoryCount}, modifiers={activeModifiers} (inputFingerCount={fingerCount}, total loaded gestures={sourceGestures.Count})");
                return null;
            }

            var globalMatchStrategy = NormalizeGlobalMatchStrategy(Configuration.AppConfig.TrajectoryMatchStrategy);
            var primaryGestures = gestures.Where(g => ResolveEffectiveMatchStrategy(g, globalMatchStrategy) == globalMatchStrategy).ToList();
            var secondaryGestures = gestures.Where(g => ResolveEffectiveMatchStrategy(g, globalMatchStrategy) != globalMatchStrategy).ToList();

            string matched = TryGetGestureSetNameMatchByStrategy(points, fingerCount, primaryGestures, globalMatchStrategy, indexSnapshot);
            if (!string.IsNullOrEmpty(matched))
                return matched;

            if (secondaryGestures.Count > 0)
            {
                var secondaryStrategy = globalMatchStrategy == FingerMatchStrategy.FeatureFinger
                    ? FingerMatchStrategy.AllFingers
                    : FingerMatchStrategy.FeatureFinger;
                matched = TryGetGestureSetNameMatchByStrategy(points, fingerCount, secondaryGestures, secondaryStrategy, indexSnapshot);
                if (!string.IsNullOrEmpty(matched))
                    return matched;
            }

            _matchedGestureId = null;
            return null;
        }

        internal static int GetLookupFingerCount(int fingerCount, int trajectoryCount)
        {
            if (trajectoryCount <= 0)
                return fingerCount;

            if (fingerCount <= 0)
                return trajectoryCount;

            // Ignore transient peak finger spikes (e.g. 5 -> 4 trajectories) for trajectory matching lookup.
            return fingerCount > trajectoryCount ? trajectoryCount : fingerCount;
        }

        private string TryGetGestureSetNameMatchByStrategy(
            Point[][] points,
            int fingerCount,
            List<IGesture> gestures,
            FingerMatchStrategy strategy,
            GestureIndexSnapshot indexSnapshot)
        {
            if (gestures == null || gestures.Count == 0)
                return null;

            if (strategy == FingerMatchStrategy.FeatureFinger)
                return GetGestureSetNameMatchByFeatureFinger(points, fingerCount, gestures);

            return GetGestureSetNameMatchByAllFingers(points, fingerCount, gestures, indexSnapshot);
        }

        private string GetGestureSetNameMatchByAllFingers(
            Point[][] points,
            int fingerCount,
            List<IGesture> gestures,
            GestureIndexSnapshot indexSnapshot)
        {
            int trajectoryCount = points.Length;

            if (trajectoryCount >= 3)
            {
                return GetGestureSetNameMatchWithTrajectoryAssignment(points, fingerCount, gestures);
            }

            // Perform pattern matching for each trajectory
            List<PointPatternMatchResult>[] comparisonResults = new List<PointPatternMatchResult>[trajectoryCount];
            for (int i = 0; i < trajectoryCount; i++)
            {
                PointsPatternSet[] patternSet;
                int cacheFingerCount = gestures.Count > 0 ? gestures[0].FingerCount : fingerCount;
                if (indexSnapshot?.PatternSetCache != null &&
                    indexSnapshot.PatternSetCache.TryGetValue((cacheFingerCount, trajectoryCount, i), out var cachedPatternSet) &&
                    cachedPatternSet.Length == gestures.Count)
                {
                    patternSet = cachedPatternSet;
                }
                else
                {
                    patternSet = new PointsPatternSet[gestures.Count];
                    for (int j = 0; j < gestures.Count; j++)
                    {
                        patternSet[j] = new PointsPatternSet(gestures[j].Name, gestures[j].PointPatterns[0].Points[i]);
                    }
                }

                gestureAnalyzer.PointPatternSet = patternSet;
                comparisonResults[i] = new List<PointPatternMatchResult>(gestures.Count);
                comparisonResults[i].AddRange(gestureAnalyzer.GetPointPatternMatchResults(points[i]));
            }

            // Filter gestures that meet probability threshold across ALL trajectories
            double threshold = Configuration.AppConfig.GestureMatchProbability;
            HashSet<int> validIndices = new HashSet<int>(Enumerable.Range(0, gestures.Count));

            for (int trajectoryIdx = 0; trajectoryIdx < trajectoryCount; trajectoryIdx++)
            {
                var matchResults = comparisonResults[trajectoryIdx];
                validIndices.RemoveWhere(gestureIdx => matchResults[gestureIdx].Probability <= threshold);

                // Early exit if no gestures pass threshold
                if (validIndices.Count == 0)
                {
                    _matchedGestureId = null;
                    if (matchResults.Count > 0)
                    {
                        var topMatch = matchResults.OrderByDescending(r => r.Probability).First();
                        Logging.LogTrace($"[GestureMatch] No gesture passed threshold={threshold}% at trajectory {trajectoryIdx}, best={topMatch.Probability:F1}% (angular={topMatch.AngularProbability:F1}%, penalty={topMatch.StructuralPenalty:F1}%) name={topMatch.Name}");
                    }
                    return null;
                }
            }

            // Calculate total probability for each matching gesture
            string bestMatch = null;
            double bestProbability = double.MinValue;

            foreach (int gestureIdx in validIndices)
            {
                IGesture gesture = gestures[gestureIdx];
                double totalProbability = 0;
                for (int i = 0; i < trajectoryCount; i++)
                {
                    totalProbability += comparisonResults[i][gestureIdx].Probability;
                }

                if (totalProbability > bestProbability)
                {
                    bestMatch = gesture.Name;
                    bestProbability = totalProbability;
                    _matchedGestureId = gesture.Id;
                }
            }

            return bestMatch;
        }

        public static FingerMatchStrategy ResolveEffectiveMatchStrategy(IGesture gesture, FingerMatchStrategy globalMatchStrategy)
        {
            var gestureStrategy = gesture?.MatchStrategy ?? FingerMatchStrategy.Inherit;
            if (gestureStrategy == FingerMatchStrategy.AllFingers ||
                gestureStrategy == FingerMatchStrategy.FeatureFinger)
            {
                return gestureStrategy;
            }

            return NormalizeGlobalMatchStrategy(globalMatchStrategy);
        }

        public static FingerMatchStrategy NormalizeGlobalMatchStrategy(FingerMatchStrategy strategy)
        {
            return strategy == FingerMatchStrategy.FeatureFinger
                ? FingerMatchStrategy.FeatureFinger
                : FingerMatchStrategy.AllFingers;
        }

        private string GetGestureSetNameMatchByFeatureFinger(Point[][] points, int fingerCount, List<IGesture> gestures)
        {
            int trajectoryCount = points.Length;
            int featureTrajectoryIndex = GetFeatureFingerTrajectoryIndex(trajectoryCount);
            double threshold = Configuration.AppConfig.GestureMatchProbability;
            const double secondaryViewFallbackDeficit = 3.0;

            var patternSet = new PointsPatternSet[gestures.Count];
            for (int gestureIdx = 0; gestureIdx < gestures.Count; gestureIdx++)
            {
                var gesture = gestures[gestureIdx];
                patternSet[gestureIdx] = new PointsPatternSet(gesture.Name, gesture.PointPatterns[0].Points[featureTrajectoryIndex]);
            }

            gestureAnalyzer.PointPatternSet = patternSet;
            var matchResults = gestureAnalyzer.GetPointPatternMatchResults(points[featureTrajectoryIndex]).ToList();

            string bestMatch = null;
            double bestProbability = double.MinValue;
            PointPatternMatchResult topResult = null;

            for (int gestureIdx = 0; gestureIdx < matchResults.Count; gestureIdx++)
            {
                var result = matchResults[gestureIdx];
                if (result.Probability <= threshold)
                {
                    if (topResult == null || result.Probability > topResult.Probability)
                        topResult = result;
                    continue;
                }

                if (result.Probability > bestProbability)
                {
                    bestProbability = result.Probability;
                    bestMatch = gestures[gestureIdx].Name;
                    _matchedGestureId = gestures[gestureIdx].Id;
                }
            }

            if (bestMatch == null)
            {
                _matchedGestureId = null;
                if (topResult != null)
                {
                    Logging.LogTrace($"[GestureMatch] No gesture passed threshold={threshold}% at feature trajectory {featureTrajectoryIndex}, best={topResult.Probability:F1}% (angular={topResult.AngularProbability:F1}%, penalty={topResult.StructuralPenalty:F1}%) name={topResult.Name}");

                    // Same threshold, different view: when feature lane is a near miss, retry using all-fingers.
                    if (trajectoryCount >= 3 && threshold - topResult.Probability <= secondaryViewFallbackDeficit)
                    {
                        string fallbackMatch = GetGestureSetNameMatchWithTrajectoryAssignment(points, fingerCount, gestures);
                        if (!string.IsNullOrEmpty(fallbackMatch))
                        {
                            Logging.LogTrace($"[GestureMatch] Feature view near-threshold miss recovered by all-fingers view: featureBest={topResult.Probability:F1}%, threshold={threshold}%");
                            return fallbackMatch;
                        }
                    }
                }
            }

            return bestMatch;
        }

        public static int GetFeatureFingerTrajectoryIndex(int trajectoryCount)
        {
            // 2 指取最左手指（index 0），3+ 指取第二根手指（index 1）
            if (trajectoryCount <= 2)
                return 0;
            return 1;
        }

        private string GetGestureSetNameMatchWithTrajectoryAssignment(Point[][] points, int fingerCount, List<IGesture> gestures)
        {
            double threshold = Configuration.AppConfig.GestureMatchProbability;
            string bestMatch = null;
            string bestFailureName = null;
            PointPatternMatchResult bestFailureResult = null;
            int bestFailureTrajectoryIndex = -1;
            double bestProbability = double.MinValue;
            double bestFailureProbability = double.MinValue;

            for (int gestureIdx = 0; gestureIdx < gestures.Count; gestureIdx++)
            {
                IGesture gesture = gestures[gestureIdx];
                var assignment = GetBestTrajectoryAssignment(points, gesture);
                if (assignment.AssignedResults == null || assignment.AssignedResults.Length == 0)
                    continue;

                bool valid = true;
                for (int inputIdx = 0; inputIdx < assignment.AssignedResults.Length; inputIdx++)
                {
                    if (assignment.AssignedResults[inputIdx].Probability <= threshold)
                    {
                        valid = false;
                        if (assignment.AssignedResults[inputIdx].Probability > bestFailureProbability)
                        {
                            bestFailureProbability = assignment.AssignedResults[inputIdx].Probability;
                            bestFailureResult = assignment.AssignedResults[inputIdx];
                            bestFailureTrajectoryIndex = inputIdx;
                            bestFailureName = gesture.Name;
                        }

                        break;
                    }
                }

                if (!valid)
                    continue;

                if (assignment.TotalProbability > bestProbability)
                {
                    bestProbability = assignment.TotalProbability;
                    bestMatch = gesture.Name;
                    _matchedGestureId = gesture.Id;
                }
            }

            if (bestMatch == null)
            {
                _matchedGestureId = null;
                if (bestFailureResult != null)
                {
                    Logging.LogTrace($"[GestureMatch] No gesture passed threshold={threshold}% at trajectory {bestFailureTrajectoryIndex}, best={bestFailureResult.Probability:F1}% (angular={bestFailureResult.AngularProbability:F1}%, penalty={bestFailureResult.StructuralPenalty:F1}%) name={bestFailureName ?? bestFailureResult.Name}");
                }
            }

            return bestMatch;
        }

        private (PointPatternMatchResult[] AssignedResults, double TotalProbability) GetBestTrajectoryAssignment(Point[][] inputPoints, IGesture gesture)
        {
            int trajectoryCount = inputPoints.Length;
            var matrix = new PointPatternMatchResult[trajectoryCount][];

            var patternSet = new PointsPatternSet[trajectoryCount];
            for (int patternIdx = 0; patternIdx < trajectoryCount; patternIdx++)
            {
                patternSet[patternIdx] = new PointsPatternSet(gesture.Name, gesture.PointPatterns[0].Points[patternIdx]);
            }

            gestureAnalyzer.PointPatternSet = patternSet;

            for (int inputIdx = 0; inputIdx < trajectoryCount; inputIdx++)
            {
                matrix[inputIdx] = gestureAnalyzer.GetPointPatternMatchResults(inputPoints[inputIdx]);
            }

            int[] assignment = FindBestTrajectoryAssignment(matrix);
            if (assignment == null)
                return (Array.Empty<PointPatternMatchResult>(), 0);

            var assignedResults = new PointPatternMatchResult[trajectoryCount];
            double totalProbability = 0;

            for (int inputIdx = 0; inputIdx < trajectoryCount; inputIdx++)
            {
                int patternIdx = assignment[inputIdx];
                var result = matrix[inputIdx][patternIdx];
                assignedResults[inputIdx] = result;
                totalProbability += result.Probability;
            }

            return (assignedResults, totalProbability);
        }

        public static int[] FindBestTrajectoryAssignment(PointPatternMatchResult[][] matrix)
        {
            if (matrix == null || matrix.Length == 0)
                return null;

            int size = matrix.Length;
            if (matrix.Any(row => row == null || row.Length != size))
                return null;

            var current = new int[size];
            var best = new int[size];
            var used = new bool[size];
            double bestTotal = double.MinValue;

            void Dfs(int rowIndex, double total)
            {
                if (rowIndex == size)
                {
                    if (total > bestTotal)
                    {
                        bestTotal = total;
                        Array.Copy(current, best, size);
                    }

                    return;
                }

                for (int colIndex = 0; colIndex < size; colIndex++)
                {
                    if (used[colIndex])
                        continue;

                    used[colIndex] = true;
                    current[rowIndex] = colIndex;
                    Dfs(rowIndex + 1, total + matrix[rowIndex][colIndex].Probability);
                    used[colIndex] = false;
                }
            }

            Dfs(0, 0);
            return bestTotal == double.MinValue ? null : best;
        }

        public string GetMostSimilarGestureName(PointPattern[] pointPattern, GestureModifiers modifiers = GestureModifiers.Default)
        {
            if (pointPattern == null || pointPattern.Length == 0)
                return null;

            return GetGestureSetNameMatch(pointPattern[0].Points, pointPattern[0].FingerCount, GetGesturesListSnapshot(), modifiers);
        }

        public string GetMostSimilarGestureName(IGesture gesture)
        {
            return GetMostSimilarGestureName(gesture.PointPatterns, gesture.Modifiers);
        }

        public string[] GetAvailableGestures()
        {
            return Gestures.OrderBy(g => g.Name).GroupBy(g => g.Name).Select(g => g.Key).ToArray();
        }

        public IEnumerable<GestureDefinitionRef> GetGestureDefinitions()
        {
            return Gestures.Select(g => new GestureDefinitionRef
            {
                Id = g.Id,
                Name = g.Name,
                Kind = GestureDefinitionKind.Trajectory,
                FingerCount = g.FingerCount,
            });
        }
        public bool GestureExists(string gestureName)
        {
            if (string.IsNullOrEmpty(gestureName))
                return false;

            return Gestures.Any(g => string.Equals(g.Name, gestureName, StringComparison.Ordinal));
        }

        public IGesture GetNewestGestureSample(string gestureName)
        {
            return String.IsNullOrEmpty(gestureName) ? null : Gestures.LastOrDefault(g => String.Equals(g.Name, gestureName, StringComparison.Ordinal));
        }

        public IGesture GetGestureById(string gestureId)
        {
            return string.IsNullOrEmpty(gestureId) ? null : Gestures.LastOrDefault(g => string.Equals(g.Id, gestureId, StringComparison.Ordinal));
        }

        public void DeleteGesture(string gestureName)
        {
            var snapshot = Gestures.ToList();
            snapshot.RemoveAll(g => g.Name.Trim() == gestureName.Trim());
            SetGesturesSnapshot(snapshot);
        }

        public void DeleteGestureById(string gestureId)
        {
            if (string.IsNullOrEmpty(gestureId)) return;
            var snapshot = Gestures.ToList();
            snapshot.RemoveAll(g => g.Id == gestureId);
            SetGesturesSnapshot(snapshot);
        }

        public string GetNewGestureName()
        {
            Random random = new Random();
            string newName;
            do
            {
                newName = GetRandomString(random, 6);
            } while (GestureExists(newName));
            return newName;
        }

        public string GetNewGestureId(PointPattern[] pointPatterns)
        {
            if (pointPatterns == null || pointPatterns.Length == 0)
                return Guid.NewGuid().ToString("N");

            string features = "";
            foreach (var pattern in pointPatterns)
            {
                features += GetPatternFeatures(pattern.Points);
            }
            int num = 0;
            string newId = features;
            var gestures = Gestures;
            while (gestures.Any(g => string.Equals(g.Id, newId, StringComparison.Ordinal)))
            {
                newId = features + num;
                num++;
            };
            return newId;
        }

        private static string GetDeterministicGestureId(string gestureName, PointPattern[] pointPatterns, int fingerCount)
        {
            if (pointPatterns != null && pointPatterns.Length > 0)
            {
                string features = "";
                foreach (var pattern in pointPatterns)
                {
                    features += GetPatternFeatures(pattern.Points);
                }

                return features;
            }

            string seed = $"{gestureName}|{fingerCount}";
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
            return $"legacy_{Convert.ToHexString(hash, 0, 4)}";
        }

        public static string GetPatternFeatures(Point[][] pattern)
        {
            string output = "";
            for (int i = 0; i < pattern.Length; i++)
            {
                var currentStroke = pattern[i];
                if (currentStroke.Length == 1)
                {
                    output += 0;
                    continue;
                }
                var strokeFeature = PointPatternMath.GetPointArrayAngularMargins(PointPatternMath.GetInterpolatedPointArray(currentStroke, 9));
                uint feature = 0;
                for (int j = 0; j < strokeFeature.Length; j++)
                {
                    var feat = strokeFeature[j] / Math.PI + 1 - 3.0 / 8.0;
                    if (feat < 0)
                    {
                        feat = 2 + feat;
                    }
                    feature = feature << 4;
                    feature |= (uint)(feat / 2 * 8) << 1;
                    feature++;
                }
                output += feature.ToString("x8");
            }

            return output;
        }

        #endregion
    }
}






