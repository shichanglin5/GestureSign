using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GestureSign.Common;
using GestureSign.Common.Configuration;
using GestureSign.Common.Gestures;
using GestureSign.Common.Input;
using GestureSign.Common.InterProcessCommunication;
using GestureSign.Common.Localization;
using GestureSign.ControlPanel.Common;
using GestureSign.PointPatterns;
using MahApps.Metro.Controls;

namespace GestureSign.ControlPanel.Dialogs
{
    public partial class GestureSimilarityTestDialog : MetroWindow, INotifyPropertyChanged
    {
        private readonly List<GestureItem> _selectedGestures;
        private bool _isTrainingActive;
        private int _trainingRequestVersion;
        private IGesture _recordedGesture;
        private DrawingImage _recordedGestureImage;
        private string _recordedGestureTypeText = LocalizationProvider.Instance.GetTextValue("SimilarityTest.NotRecorded");
        private string _statusText = LocalizationProvider.Instance.GetTextValue("SimilarityTest.ClickRecordHint");
        private double _matchThreshold;

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<GestureSimilarityCandidate> Candidates { get; } = new ObservableCollection<GestureSimilarityCandidate>();

        public DrawingImage RecordedGestureImage
        {
            get => _recordedGestureImage;
            private set
            {
                if (!ReferenceEquals(_recordedGestureImage, value))
                {
                    _recordedGestureImage = value;
                    OnPropertyChanged(nameof(RecordedGestureImage));
                }
            }
        }

        public string RecordedGestureTypeText
        {
            get => _recordedGestureTypeText;
            private set
            {
                if (_recordedGestureTypeText != value)
                {
                    _recordedGestureTypeText = value;
                    OnPropertyChanged(nameof(RecordedGestureTypeText));
                }
            }
        }

        public string StatusText
        {
            get => _statusText;
            private set
            {
                if (_statusText != value)
                {
                    _statusText = value;
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        public double MatchThreshold
        {
            get => _matchThreshold;
            set
            {
                if (Math.Abs(_matchThreshold - value) > double.Epsilon)
                {
                    _matchThreshold = value;
                    OnPropertyChanged(nameof(MatchThreshold));
                    OnPropertyChanged(nameof(MatchThresholdText));
                    RefreshCandidateMatchState();
                }
            }
        }

        public string MatchThresholdText => $"{MatchThreshold:F0}%";

        public GestureSimilarityTestDialog(IEnumerable<GestureItem> selectedGestures)
        {
            InitializeComponent();
            DataContext = this;
            _selectedGestures = selectedGestures?.Where(item => item?.Gesture != null).ToList() ?? new List<GestureItem>();
            MatchThreshold = AppConfig.GestureMatchProbability;
            InitializeCandidates();
            UpdateUiState();
        }

        private void InitializeCandidates()
        {
            Candidates.Clear();

            foreach (var gestureItem in _selectedGestures)
            {
                Candidates.Add(CreateCandidate(gestureItem, MatchComputationResult.Empty));
            }
        }

        private void RecordButton_Click(object sender, RoutedEventArgs e)
        {
            SetTrainingState(!_isTrainingActive);
        }

        private void MatchButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshCandidatesAndStatus(requireRecordedGesture: true);
            UpdateUiState();
        }

        private void DetailButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !(button.Tag is GestureSimilarityCandidate candidate))
                return;

            ShowDetailDialog(candidate);
        }

        private void GesturePreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2)
                return;

            if (!(sender is FrameworkElement element) || !(element.DataContext is GestureSimilarityCandidate candidate))
                return;

            e.Handled = true;
            ShowGestureEditor(candidate);
        }

        private void MetroWindow_Closed(object sender, EventArgs e)
        {
            SetTrainingState(false);
        }

        private async void SetTrainingState(bool isActive)
        {
            if (_isTrainingActive == isActive)
            {
                UpdateUiState();
                return;
            }

            int requestVersion = ++_trainingRequestVersion;
            _isTrainingActive = isActive;

            if (isActive)
            {
                MessageProcessor.GotNewGestureDefinition -= MessageProcessor_GotNewGestureDefinition;
                MessageProcessor.GotNewGestureDefinition += MessageProcessor_GotNewGestureDefinition;
                var started = await NamedPipe.SendMessageAsync(IpcCommands.StartTeaching, Constants.Daemon);
                if (requestVersion != _trainingRequestVersion || !_isTrainingActive)
                    return;

                if (!started)
                {
                    _isTrainingActive = false;
                    MessageProcessor.GotNewGestureDefinition -= MessageProcessor_GotNewGestureDefinition;
                    StatusText = LocalizationProvider.Instance.GetTextValue("SimilarityTest.DaemonConnectionFailed");
                }
                else
                {
                    StatusText = LocalizationProvider.Instance.GetTextValue("SimilarityTest.RecordingHint");
                }
            }
            else
            {
                MessageProcessor.GotNewGestureDefinition -= MessageProcessor_GotNewGestureDefinition;
                _ = NamedPipe.SendMessageAsync(IpcCommands.StopTraining, Constants.Daemon);
                if (requestVersion != _trainingRequestVersion)
                    return;

                if (_recordedGesture == null)
                    StatusText = LocalizationProvider.Instance.GetTextValue("SimilarityTest.ClickRecordHint");
            }

            UpdateUiState();
        }

        private void MessageProcessor_GotNewGestureDefinition(object sender, RecordedGestureDefinitionResult definition)
        {
            Dispatcher.Invoke(() =>
            {
                SetTrainingState(false);

                if (definition?.Type == RecordedGestureType.Trajectory && definition.TrajectoryGesture != null)
                {
                    _recordedGesture = definition.TrajectoryGesture;
                    RecordedGestureTypeText = ContactGestureText.GetRecordedGestureTypeText(definition);
                    RecordedGestureImage = CreateGestureImage(_recordedGesture);
                    StatusText = LocalizationProvider.Instance.GetTextValue("SimilarityTest.RecordComplete");
                }
                else
                {
                    _recordedGesture = null;
                    RecordedGestureTypeText = ContactGestureText.GetRecordedGestureTypeText(definition);
                    RecordedGestureImage = CreateGestureImage(ContactGestureDisplayFactory.CreateDisplayGesture(definition));
                    StatusText = LocalizationProvider.Instance.GetTextValue("SimilarityTest.TrajectoryOnlyWarning");
                }

                InitializeCandidates();
                UpdateUiState();
            });
        }

        private void RefreshCandidateMatchState()
        {
            if (Candidates.Count == 0)
                return;

            var refreshed = Candidates
                .Select(candidate => candidate.CloneWithThreshold(MatchThreshold))
                .OrderByDescending(candidate => candidate.IsMatch)
                .ThenByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Name, StringComparer.CurrentCulture)
                .ToList();

            Candidates.Clear();
            foreach (var candidate in refreshed)
            {
                Candidates.Add(candidate);
            }

            UpdateStatusText(refreshed);
        }

        private void RefreshCandidatesAndStatus(bool requireRecordedGesture)
        {
            bool hasRecordedGesture = _recordedGesture?.PointPatterns != null && _recordedGesture.PointPatterns.Length > 0;
            if (requireRecordedGesture && !hasRecordedGesture)
            {
                StatusText = LocalizationProvider.Instance.GetTextValue("SimilarityTest.RecordFirst");
                return;
            }

            var candidates = _selectedGestures
                .Select(item =>
                {
                    var matchResult = hasRecordedGesture ? CalculateSimilarityScore(item.Gesture, _recordedGesture) : MatchComputationResult.Empty;
                    return CreateCandidate(item, matchResult);
                });

            var orderedCandidates = hasRecordedGesture
                ? candidates
                    .OrderByDescending(item => item.IsMatch)
                    .ThenByDescending(item => item.Score)
                    .ThenBy(item => item.Name, StringComparer.CurrentCulture)
                    .ToList()
                : candidates.ToList();

            Candidates.Clear();
            foreach (var candidate in orderedCandidates)
            {
                Candidates.Add(candidate);
            }

            UpdateStatusText(hasRecordedGesture ? orderedCandidates : null);
        }

        private void UpdateStatusText(IReadOnlyList<GestureSimilarityCandidate> candidates)
        {
            if (candidates == null)
                return;

            if (candidates.Count == 0)
            {
                StatusText = LocalizationProvider.Instance.GetTextValue("SimilarityTest.NoComparableGestures");
                return;
            }

            int matchedCount = candidates.Count(candidate => candidate.IsMatch);
            var bestMatched = candidates.FirstOrDefault(candidate => candidate.IsMatch);
            if (bestMatched != null)
            {
                StatusText = string.Format(LocalizationProvider.Instance.GetTextValue("SimilarityTest.BestMatch"), bestMatched.Name, bestMatched.Score, matchedCount);
                return;
            }

            var topCandidate = candidates[0];
            StatusText = string.Format(LocalizationProvider.Instance.GetTextValue("SimilarityTest.NoMatchBelowThreshold"), topCandidate.Name, topCandidate.Score);
        }

        private void ShowDetailDialog(GestureSimilarityCandidate candidate)
        {
            if (candidate == null)
                return;

            var dialog = new SimilarityScoreDetailDialog(candidate)
            {
                Owner = this,
            };

            dialog.ShowDialog();
        }

        private void ShowGestureEditor(GestureSimilarityCandidate candidate)
        {
            var gesture = ResolveGesture(candidate);
            if (gesture == null)
            {
                StatusText = string.Format(LocalizationProvider.Instance.GetTextValue("SimilarityTest.GestureNotFound"), candidate?.Name);
                return;
            }

            var dialog = new GestureDefinition(gesture)
            {
                Owner = this,
            };

            var result = dialog.ShowDialog();
            if (result.GetValueOrDefault())
            {
                SyncEditedGesture(candidate, dialog.CurrentGesture);
                RefreshCandidatesAndStatus(requireRecordedGesture: false);
            }
        }

        private IGesture ResolveGesture(GestureSimilarityCandidate candidate)
        {
            if (candidate == null)
                return null;

            if (!string.IsNullOrEmpty(candidate.GestureId))
            {
                var gestureById = GestureManager.Instance.GetGestureById(candidate.GestureId);
                if (gestureById != null)
                    return gestureById;
            }

            return GestureManager.Instance.GetNewestGestureSample(candidate.Name);
        }

        private void SyncEditedGesture(GestureSimilarityCandidate candidate, IGesture editedGesture)
        {
            if (candidate == null || editedGesture == null)
                return;

            var gestureItem = _selectedGestures.FirstOrDefault(item =>
                item?.Gesture != null &&
                ((!string.IsNullOrEmpty(candidate.GestureId) && string.Equals(item.Gesture.Id, candidate.GestureId, StringComparison.Ordinal)) ||
                 string.Equals(item.Gesture.Name, candidate.Name, StringComparison.Ordinal)));

            if (gestureItem == null)
                return;

            gestureItem.Gesture = editedGesture;
            gestureItem.GestureImage = CreateGestureImage(editedGesture);
            gestureItem.PatternCount = editedGesture.PointPatterns?.Length ?? 0;
        }

        private MatchComputationResult CalculateSimilarityScore(IGesture candidateGesture, IGesture recordedGesture)
        {
            if (candidateGesture?.PointPatterns == null || recordedGesture?.PointPatterns == null)
                return MatchComputationResult.Empty;

            if (candidateGesture.FingerCount != recordedGesture.FingerCount)
                return MatchComputationResult.Empty;

            if (candidateGesture.PointPatterns.Length != recordedGesture.PointPatterns.Length)
                return MatchComputationResult.Empty;

            var totalScore = 0d;
            var totalTrajectoryCount = 0;
            var minimumTrajectoryScore = double.MaxValue;
            var detailItems = new List<GestureSimilarityScoreDetail>();

            for (int levelIndex = 0; levelIndex < candidateGesture.PointPatterns.Length; levelIndex++)
            {
                var candidatePattern = candidateGesture.PointPatterns[levelIndex];
                var recordedPattern = recordedGesture.PointPatterns[levelIndex];

                if (candidatePattern?.Points == null || recordedPattern?.Points == null)
                    return MatchComputationResult.Empty;

                if (candidatePattern.Points.Length != recordedPattern.Points.Length)
                    return MatchComputationResult.Empty;

                for (int trajectoryIndex = 0; trajectoryIndex < candidatePattern.Points.Length; trajectoryIndex++)
                {
                    var analyzer = new PointPatternAnalyzer()
                    {
                        TapThreshold = AppConfig.TapDistanceThreshold,
                    };

                    var breakdown = analyzer.GetPointPatternMatchBreakdown(
                        new PointsPatternSet(candidateGesture.Name, candidatePattern.Points[trajectoryIndex]),
                        new PointsPatternSet(recordedGesture.Name, recordedPattern.Points[trajectoryIndex]));
                    double probability = breakdown?.Probability ?? 0d;
                    totalScore += probability;
                    minimumTrajectoryScore = Math.Min(minimumTrajectoryScore, probability);
                    totalTrajectoryCount++;
                    detailItems.Add(new GestureSimilarityScoreDetail(levelIndex + 1, trajectoryIndex + 1, breakdown));
                }
            }

            if (totalTrajectoryCount == 0)
                return MatchComputationResult.Empty;

            return new MatchComputationResult(totalScore / totalTrajectoryCount, minimumTrajectoryScore, detailItems);
        }

        private GestureSimilarityCandidate CreateCandidate(GestureItem gestureItem, MatchComputationResult matchResult)
        {
            return new GestureSimilarityCandidate(
                gestureItem?.Gesture?.Id,
                gestureItem?.Gesture?.Name ?? string.Empty,
                gestureItem?.Gesture?.FingerCount ?? 0,
                gestureItem?.GestureImage ?? CreateGestureImage(gestureItem?.Gesture),
                matchResult.AverageScore,
                matchResult.MinimumTrajectoryScore,
                matchResult.DetailItems,
                MatchThreshold);
        }

        private DrawingImage CreateGestureImage(IGesture gesture)
        {
            if (gesture?.PointPatterns == null)
                return null;

            var accentBrush = TryFindResource("MahApps.Brushes.Highlight") as SolidColorBrush;
            var color = accentBrush?.Color ?? Colors.DodgerBlue;
            return GestureImage.CreateImage(gesture.PointPatterns, new Size(180, 140), color);
        }

        private void UpdateUiState()
        {
            RecordButton.Content = _isTrainingActive ? LocalizationProvider.Instance.GetTextValue("SimilarityTest.Stop") : LocalizationProvider.Instance.GetTextValue("SimilarityTest.Record");
            MatchButton.IsEnabled = _recordedGesture?.PointPatterns != null && _recordedGesture.PointPatterns.Length > 0 && !_isTrainingActive;
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public sealed class GestureSimilarityCandidate
        {
            public GestureSimilarityCandidate(string gestureId, string name, int fingerCount, DrawingImage gestureImage, double score, double minimumTrajectoryScore, IReadOnlyList<GestureSimilarityScoreDetail> detailItems, double threshold)
            {
                GestureId = gestureId;
                Name = name;
                FingerCount = fingerCount;
                GestureImage = gestureImage;
                Score = score;
                MinimumTrajectoryScore = minimumTrajectoryScore;
                DetailItems = detailItems ?? Array.Empty<GestureSimilarityScoreDetail>();
                Threshold = threshold;
            }

            public string GestureId { get; }
            public string Name { get; }
            public int FingerCount { get; }
            public DrawingImage GestureImage { get; }
            public double Score { get; }
            public double MinimumTrajectoryScore { get; }
            public IReadOnlyList<GestureSimilarityScoreDetail> DetailItems { get; }
            public double Threshold { get; }

            public string FingerCountText => FingerCount > 0 ? string.Format(LocalizationProvider.Instance.GetTextValue("SimilarityTest.FingerCountFormat"), FingerCount) : string.Empty;
            public string ScoreText => $"{Score:F1}%";
            public string MinimumScoreText => $"{MinimumTrajectoryScore:F1}%";
            public bool IsMatch => MinimumTrajectoryScore > Threshold;
            public string MatchStateText => IsMatch ? LocalizationProvider.Instance.GetTextValue("SimilarityTest.Matched") : LocalizationProvider.Instance.GetTextValue("SimilarityTest.NotMatched");

            public GestureSimilarityCandidate CloneWithThreshold(double threshold)
            {
                return new GestureSimilarityCandidate(GestureId, Name, FingerCount, GestureImage, Score, MinimumTrajectoryScore, DetailItems, threshold);
            }
        }

        public sealed class GestureSimilarityScoreDetail
        {
            public GestureSimilarityScoreDetail(int level, int trajectory, PointPatternScoreBreakdown breakdown)
            {
                Level = level;
                Trajectory = trajectory;
                Breakdown = breakdown ?? new PointPatternScoreBreakdown();
            }

            public int Level { get; }

            public int Trajectory { get; }

            public PointPatternScoreBreakdown Breakdown { get; }

            public string LevelText => $"L{Level}";

            public string TrajectoryText => $"T{Trajectory}";

            public string MatchModeText => Breakdown.CompareToIsTap || Breakdown.PointsIsTap ? "Tap" : LocalizationProvider.Instance.GetTextValue("SimilarityTest.Trajectory");

            public string ProbabilityText => $"{Breakdown.Probability:F1}%";

            public string AngularProbabilityText => $"{Breakdown.AngularProbability:F1}%";

            public string AverageAngularDeltaText => $"{Breakdown.AverageAngularDeltaDegrees:F1}°";

            public string StructuralPenaltyText => $"{Breakdown.StructuralPenalty:F1}";

            public string SignedTurnPenaltyText => $"{Breakdown.SignedTurnPenalty:F1}";

            public string AbsoluteTurnPenaltyText => $"{Breakdown.AbsoluteTurnPenalty:F1}";

            public string SignedTurnDeltaText => $"{Breakdown.SignedTurnDeltaDegrees:F1}°";

            public string AbsoluteTurnDeltaText => $"{Breakdown.AbsoluteTurnDeltaDegrees:F1}°";

            public string TapInfoText
            {
                get
                {
                    if (!Breakdown.CompareToIsTap && !Breakdown.PointsIsTap)
                        return LocalizationProvider.Instance.GetTextValue("SimilarityTest.TrajectoryComparison");

                    return string.Format(LocalizationProvider.Instance.GetTextValue("SimilarityTest.TemplateTapInfo"), Breakdown.CompareToIsTap, Breakdown.PointsIsTap, Breakdown.IsTapMatch);
                }
            }
        }

        private readonly struct MatchComputationResult
        {
            public static MatchComputationResult Empty { get; } = new MatchComputationResult(0d, 0d, Array.Empty<GestureSimilarityScoreDetail>());

            public MatchComputationResult(double averageScore, double minimumTrajectoryScore, IReadOnlyList<GestureSimilarityScoreDetail> detailItems)
            {
                AverageScore = averageScore;
                MinimumTrajectoryScore = minimumTrajectoryScore;
                DetailItems = detailItems ?? Array.Empty<GestureSimilarityScoreDetail>();
            }

            public double AverageScore { get; }
            public double MinimumTrajectoryScore { get; }
            public IReadOnlyList<GestureSimilarityScoreDetail> DetailItems { get; }
        }
    }
}
