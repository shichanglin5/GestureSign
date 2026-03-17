using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using GestureSign.ControlPanel.Common;
using MahApps.Metro.Controls;

namespace GestureSign.ControlPanel.Dialogs
{
    public partial class MergeGesturesDialog : MetroWindow, INotifyPropertyChanged
    {
        private MergeGestureOption _selectedOption;

        public MergeGesturesDialog(IEnumerable<GestureItem> gestures)
        {
            InitializeComponent();
            Options = new ObservableCollection<MergeGestureOption>((gestures ?? Enumerable.Empty<GestureItem>()).Select(item => new MergeGestureOption(item)));
            DataContext = this;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<MergeGestureOption> Options { get; }

        public MergeGestureOption SelectedOption
        {
            get => _selectedOption;
            set
            {
                if (ReferenceEquals(_selectedOption, value))
                    return;

                _selectedOption = value;
                OnPropertyChanged(nameof(SelectedOption));
                OnPropertyChanged(nameof(CanMerge));
                OnPropertyChanged(nameof(MergeSummaryText));
            }
        }

        public bool CanMerge => SelectedOption != null;

        public GestureItem TargetGesture => SelectedOption?.GestureItem;

        public string MergeSummaryText => SelectedOption == null
            ? "请选择一个要保留的目标手势。"
            : $"其余 {Options.Count - 1} 个手势将合并到“{SelectedOption.Name}”。";

        private void MergeButton_Click(object sender, RoutedEventArgs e)
        {
            if (!CanMerge)
                return;

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public sealed class MergeGestureOption
        {
            public MergeGestureOption(GestureItem gestureItem)
            {
                GestureItem = gestureItem;
            }

            public GestureItem GestureItem { get; }

            public string Name => GestureItem?.Gesture?.Name ?? string.Empty;

            public string FingerCountText => GestureItem == null ? string.Empty : $"{GestureItem.FingerCountText} · 轨迹数 {GestureItem.PatternCount}";

            public string ApplicationsSummary => string.IsNullOrWhiteSpace(GestureItem?.Applications) ? "当前没有绑定动作" : $"已绑定: {GestureItem.Applications}";

            public DrawingImage GestureImage => GestureItem?.GestureImage;
        }
    }
}
