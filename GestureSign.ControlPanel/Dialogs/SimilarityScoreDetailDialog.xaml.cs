using System.Collections.Generic;
using System.Linq;
using System.Windows;
using MahApps.Metro.Controls;

namespace GestureSign.ControlPanel.Dialogs
{
    public partial class SimilarityScoreDetailDialog : MetroWindow
    {
        public SimilarityScoreDetailDialog(GestureSimilarityTestDialog.GestureSimilarityCandidate candidate)
        {
            InitializeComponent();
            Candidate = candidate;
            DetailItems = candidate?.DetailItems?.ToList() ?? new List<GestureSimilarityTestDialog.GestureSimilarityScoreDetail>();
            DataContext = this;
        }

        public GestureSimilarityTestDialog.GestureSimilarityCandidate Candidate { get; }

        public IReadOnlyList<GestureSimilarityTestDialog.GestureSimilarityScoreDetail> DetailItems { get; }

        public string SummaryText => Candidate == null
            ? string.Empty
            : $"平均分 {Candidate.ScoreText}，最弱轨迹 {Candidate.MinimumScoreText}，阈值 {Candidate.Threshold:F0}% ，结果 {Candidate.MatchStateText}";

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
