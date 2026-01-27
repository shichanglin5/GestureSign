using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Drawing;
using System.Threading.Tasks;
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

        private const int GestureStackTimeout = 800;

        private int _gestureLevel = 0;

        // Create thread-safe lazy singleton instance
        private static readonly Lazy<GestureManager> _instance = new Lazy<GestureManager>(() => new GestureManager());

        // Create read/write list of IGestures to hold system gestures
        private List<IGesture> _Gestures;

        // Create PointPatternAnalyzer to process gestures when received
        PointPatternAnalyzer gestureAnalyzer = null;

        private bool _isGestureStackTimeout;
        private int? _lastGestureTime;
        private List<IGesture> _gestureMatchResult;

        #endregion

        #region Public Instance Properties

        public string GestureName { get; set; }
        public IGesture[] Gestures
        {
            get
            {
                if (_Gestures == null)
                    _Gestures = new List<IGesture>();

                return _Gestures.ToArray();
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

        #endregion

        #region Public Type Properties

        public static GestureManager Instance
        {
            get { return _instance.Value; }
        }

        #endregion

        #region Events

        protected void PointCapture_CaptureStarted(object sender, PointsCapturedEventArgs e)
        {
            if (_lastGestureTime != null && Environment.TickCount - _lastGestureTime.Value > GestureStackTimeout)
                _isGestureStackTimeout = true;
        }

        protected void PointCapture_BeforePointsCaptured(object sender, PointsCapturedEventArgs e)
        {
            var pointCapture = (IPointCapture)sender;
            if (_isGestureStackTimeout)
            {
                _lastGestureTime = null;
                _isGestureStackTimeout = false;

                _gestureLevel = 0;
                _gestureMatchResult = null;
            }

            if (pointCapture.Mode == CaptureMode.Training)
            {
                _gestureLevel = 0;
                _gestureMatchResult = null;
            }

            var sourceGesture = _gestureLevel == 0 ? _Gestures : _gestureMatchResult;
            var capturedPoints = e.Points.Select(l => l.ToArray()).ToArray();
            GestureName = GetGestureSetNameMatch(capturedPoints, e.FingerCount, sourceGesture, _gestureLevel, out _gestureMatchResult);

            if (pointCapture.Mode != CaptureMode.Training)
            {
                if (_gestureMatchResult != null && _gestureMatchResult.Count != 0)
                {
                    _gestureLevel++;
                    _lastGestureTime = Environment.TickCount;
                }
                else
                {
                    _gestureLevel = 0;
                    _gestureMatchResult = null;
                }
            }
        }

        #endregion

        #region Custom Events

        public static event EventHandler OnLoadGesturesCompleted;
        public static event EventHandler GestureSaved;

        #endregion

        #region Private Methods

        private bool LoadDefaults()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Defaults", Constants.GesturesFileName);
            _Gestures = LoadGesturesFromFile(path);

            return _Gestures != null;
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
                        _Gestures = gestures;
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

            // Wireup event to Touch capture class to catch points captured
            if (pointCapture != null)
            {
                pointCapture.BeforePointsCaptured += PointCapture_BeforePointsCaptured;
                pointCapture.CaptureStarted += PointCapture_CaptureStarted; ;
            }
        }

        public void AddGesture(IGesture Gesture)
        {
            _Gestures.Add(Gesture);
        }

        public Task LoadGestures()
        {
            Action<bool> loadCompleted =
                   result =>
                   {
                       if (!result)
                           if (!LoadBackup())
                               if (!LoadDefaults())
                                   _Gestures = new List<IGesture>();
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
                            _Gestures = legacyGestures.Cast<IGesture>().ToList();
                        }
                        else
                        {
                            _Gestures = gestures;
                        }
                    }


                    return _Gestures != null;
                }
                catch
                {
                    return false;
                }
            });

            return startLoading.ContinueWith(antecendent => loadCompleted(antecendent.Result));
        }

        public bool SaveGestures()
        {
            try
            {
                bool flag = Configuration.FileManager.SaveObject(Gestures, Path.Combine(AppConfig.ApplicationDataPath, Constants.GesturesFileName));

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

            FileManager.WaitFile(filePath);

            List<IGesture> gestureList = new List<IGesture>();
            int totalGesturesInFile = 0;
            int skippedLegacyGestures = 0;

            try
            {
                string json = File.ReadAllText(filePath);

                JsonTextReader reader = new JsonTextReader(new StringReader(json));

                while (reader.Read())
                {
                    if (reader.TokenType == JsonToken.StartObject)
                    {
                        totalGesturesInFile++;
                        Gesture gesture = new Gesture();
                        List<PointPattern> pointPatternList = new List<PointPattern>();
                        string gestureName = null;
                        int fingerCount = 0;
                        int totalPointPatternsInGesture = 0;

                        while (reader.Read())
                        {
                            if (reader.TokenType == JsonToken.EndObject)
                            {
                                // Update PointPatterns with FingerCount
                                foreach (var pp in pointPatternList)
                                {
                                    pp.FingerCount = fingerCount;
                                }

                                gesture.Name = gestureName;
                                gesture.FingerCount = fingerCount;
                                gesture.PointPatterns = pointPatternList.ToArray();

                                // Only add gesture if it has valid patterns
                                if (gesture.Name != null && gesture.PointPatterns != null && gesture.PointPatterns.Length > 0)
                                {
                                    gestureList.Add(gesture);
                                }
                                else
                                {
                                    if (totalPointPatternsInGesture > 0)
                                    {
                                        skippedLegacyGestures++;
                                    }
                                }
                                break;
                            }

                            if (reader.TokenType != JsonToken.PropertyName) continue;

                            string propertyName = (string)reader.Value;
                            if (propertyName == "Name")
                            {
                                gestureName = reader.ReadAsString();
                            }
                            else if (propertyName == "FingerCount")
                            {
                                fingerCount = reader.ReadAsInt32() ?? 0;
                            }
                            else if (propertyName == "PointPatterns")
                            {
                                // Read PointPatterns array
                                if (!reader.Read() || reader.TokenType != JsonToken.StartArray) continue;

                                while (reader.Read() && reader.TokenType != JsonToken.EndArray)
                                {
                                    if (reader.TokenType == JsonToken.StartObject)
                                    {
                                        totalPointPatternsInGesture++;
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

                                        // Only add if we have exactly 1 trajectory (skip legacy 2-trajectory format)
                                        if (strokeList != null && strokeList.Count == 1)
                                        {
                                            pointPatternList.Add(new PointPattern(strokeList.ToArray(), 0));
                                        }
                                        else if (strokeList != null && strokeList.Count > 1)
                                        {
                                            Log.Logging.LogTrace($"[LoadGesturesFromFile] Filtering out PointPattern with {strokeList.Count} trajectories (legacy 2-trajectory format)");
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


            // Log details of loaded gestures
            foreach (var gesture in gestureList)
            {
                int totalPoints = 0;
                foreach (var pp in gesture.PointPatterns)
                {
                    if (pp.Points != null)
                    {
                        totalPoints += pp.Points.Length;
                    }
                }
            }

            return gestureList;
        }

        public string GetGestureSetNameMatch(Point[][] points, int fingerCount, List<IGesture> sourceGestures, int sourceGestureLevel, out List<IGesture> matching)//PointF[]
        {
            if (points.Length == 0 || sourceGestures == null || sourceGestures.Count == 0)
            {
                matching = null;
                return null;
            }

            // Pre-filter gestures inline to avoid allocating intermediate lists
            // Reuse list instance instead of creating new one
            List<IGesture> gestures = new List<IGesture>(sourceGestures.Count);
            int trajectoryCount = points.Length;

            for (int i = 0; i < sourceGestures.Count; i++)
            {
                IGesture g = sourceGestures[i];
                if (g.PointPatterns != null &&
                    g.PointPatterns.Length > sourceGestureLevel &&
                    g.PointPatterns[sourceGestureLevel].Points != null &&
                    g.PointPatterns[sourceGestureLevel].Points.Length == trajectoryCount &&
                    g.FingerCount == fingerCount)
                {
                    gestures.Add(g);
                }
            }

            if (gestures.Count == 0)
            {
                matching = null;
                return null;
            }

            // Perform pattern matching for each trajectory
            List<PointPatternMatchResult>[] comparisonResults = new List<PointPatternMatchResult>[trajectoryCount];
            for (int i = 0; i < trajectoryCount; i++)
            {
                // Build point pattern set for this trajectory - cache to avoid rebuilding
                var patternSet = new PointsPatternSet[gestures.Count];
                for (int j = 0; j < gestures.Count; j++)
                {
                    patternSet[j] = new PointsPatternSet(gestures[j].Name, gestures[j].PointPatterns[sourceGestureLevel].Points[i]);
                }

                gestureAnalyzer.PointPatternSet = patternSet;
                comparisonResults[i] = new List<PointPatternMatchResult>(gestures.Count);
                comparisonResults[i].AddRange(gestureAnalyzer.GetPointPatternMatchResults(points[i]));
            }

            // Filter gestures that meet probability threshold across ALL trajectories
            // Using HashSet for O(1) removal instead of repeated LINQ Where().ToList()
            double threshold = Configuration.AppConfig.GestureMatchProbability;
            HashSet<int> validIndices = new HashSet<int>(Enumerable.Range(0, gestures.Count));

            for (int trajectoryIdx = 0; trajectoryIdx < trajectoryCount; trajectoryIdx++)
            {
                var matchResults = comparisonResults[trajectoryIdx];
                validIndices.RemoveWhere(gestureIdx => matchResults[gestureIdx].Probability <= threshold);

                // Early exit if no gestures pass threshold
                if (validIndices.Count == 0)
                {
                    Logging.LogInfo($"[GestureMatch] No gesture passed threshold={threshold}% at trajectory {trajectoryIdx}");
                    matching = null;
                    return null;
                }
            }

            // Separate gestures into multi-level matches and final matches
            List<IGesture> matchingResult = new List<IGesture>(validIndices.Count);
            List<KeyValuePair<string, double>> recognizedResult = new List<KeyValuePair<string, double>>(validIndices.Count);

            foreach (int gestureIdx in validIndices)
            {
                IGesture gesture = gestures[gestureIdx];
                if (gesture.PointPatterns.Length > sourceGestureLevel + 1)
                {
                    // Multi-level gesture - needs more input
                    matchingResult.Add(gesture);
                }
                else
                {
                    // Final level - calculate total probability
                    double totalProbability = 0;
                    for (int i = 0; i < trajectoryCount; i++)
                    {
                        totalProbability += comparisonResults[i][gestureIdx].Probability;
                    }

                    recognizedResult.Add(new KeyValuePair<string, double>(gesture.Name, totalProbability));
                }
            }

            matching = matchingResult.Count == 0 ? null : matchingResult;

            if (recognizedResult.Count == 0)
                return null;

            // Find best match
            string bestMatch = recognizedResult[0].Key;
            double bestProbability = recognizedResult[0].Value;

            for (int i = 1; i < recognizedResult.Count; i++)
            {
                if (recognizedResult[i].Value > bestProbability)
                {
                    bestMatch = recognizedResult[i].Key;
                    bestProbability = recognizedResult[i].Value;
                }
            }

            // Log all candidates and probabilities for debugging
            if (recognizedResult.Count > 1)
            {
                var candidates = string.Join(", ", recognizedResult.Select(r => $"{r.Key}={r.Value / trajectoryCount:F1}%"));
            }

            return bestMatch;
        }

        public string GetMostSimilarGestureName(PointPattern[] pointPattern)
        {
            string matchName = null;
            List<IGesture> matchGestures = null;
            for (int i = 0; i < pointPattern.Length;)
            {
                matchName = GetGestureSetNameMatch(pointPattern[i].Points, pointPattern[i].FingerCount, matchGestures ?? _Gestures, i, out matchGestures);

                if (++i < pointPattern.Length && matchGestures == null)
                    return null;
            }
            return matchName;
        }

        public string GetMostSimilarGestureName(IGesture gesture)
        {
            return GetMostSimilarGestureName(gesture.PointPatterns);
        }

        public string[] GetAvailableGestures()
        {
            return Gestures.OrderBy(g => g.Name).GroupBy(g => g.Name).Select(g => g.Key).ToArray();
        }

        public bool GestureExists(string gestureName)
        {
            return _Gestures.Exists(g => String.Equals(g.Name, gestureName, StringComparison.Ordinal));
        }

        public IGesture GetNewestGestureSample(string gestureName)
        {
            return String.IsNullOrEmpty(gestureName) ? null : Gestures.LastOrDefault(g => String.Equals(g.Name, gestureName, StringComparison.Ordinal));
        }

        public IGesture GetNewestGestureSample()
        {
            return GetNewestGestureSample(this.GestureName);
        }

        public void DeleteGesture(string gestureName)
        {
            _Gestures.RemoveAll(g => g.Name.Trim() == gestureName.Trim());
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
            string features = "";
            foreach (var pattern in pointPatterns)
            {
                features += GetPatternFeatures(pattern.Points);
            }
            int num = 0;
            string newId = features;
            while (GestureExists(newId))
            {
                newId = features + num;
                num++;
            };
            return newId;
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
