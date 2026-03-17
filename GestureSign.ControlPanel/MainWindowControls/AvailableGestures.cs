using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using GestureSign.Common.Extensions;
using GestureSign.Common.Gestures;
using GestureSign.Common.Input;
using GestureSign.Common.Localization;
using GestureSign.ControlPanel.Common;
using GestureSign.ControlPanel.Dialogs;
using MahApps.Metro.Controls.Dialogs;
using Microsoft.Win32;

namespace GestureSign.ControlPanel.MainWindowControls
{
    /// <summary>
    /// AvailableGestures.xaml 的交互逻辑
    /// </summary>
    public partial class AvailableGestures : UserControl
    {
        public AvailableGestures()
        {
            InitializeComponent();
        }

        private void lstAvailableGestures_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            btnEditGesture.IsEnabled = lstAvailableGestures.SelectedItems.Count == 1;
            btnDelGesture.IsEnabled = lstAvailableGestures.SelectedItems.Count > 0;
            btnMatchTest.IsEnabled = lstAvailableGestures.SelectedItems.Count > 0;
            btnMergeGesture.IsEnabled = lstAvailableGestures.SelectedItems.Count > 1;
        }

        private void btnDelGesture_Click(object sender, RoutedEventArgs e)
        {
            // Make sure at least one item is selected
            if (lstAvailableGestures.SelectedItems.Count == 0) return;

            var selectedGestures = lstAvailableGestures.SelectedItems.Cast<GestureItem>().ToList();
            var gestureCount = selectedGestures.Count;
            var confirmMessage = gestureCount == 1
                ? LocalizationProvider.Instance.GetTextValue("Gesture.Messages.DeleteGestureConfirm")
                : string.Format("确定要删除这 {0} 个手势吗？", gestureCount);

            var parentWindow = UIHelper.GetParentWindow(this);
            if (parentWindow == null)
            {
                // Log the error - parent window not found
                GestureSign.Common.Log.Logging.LogWarning("[AvailableGestures] btnDelGesture_Click - Parent window is null, cannot show dialog");
                return;
            }

            if (parentWindow.ShowModalMessageExternal(
                        LocalizationProvider.Instance.GetTextValue("Gesture.Messages.DeleteConfirmTitle"),
                        confirmMessage,
                        MessageDialogStyle.AffirmativeAndNegative,
                        new MetroDialogSettings()
                        {
                            AffirmativeButtonText = LocalizationProvider.Instance.GetTextValue("Common.OK"),
                            NegativeButtonText = LocalizationProvider.Instance.GetTextValue("Common.Cancel"),
                        }) == MessageDialogResult.Affirmative)
            {
                foreach (GestureItem listItem in selectedGestures)
                {
                    GestureManager.Instance.DeleteGesture(listItem.Gesture.Name);
                }

                GestureManager.Instance.SaveGestures();
            }
        }
        private void btnEditGesture_Click(object sender, RoutedEventArgs e)
        {
            EditGesture();
        }

        private void btnMatchTest_Click(object sender, RoutedEventArgs e)
        {
            if (lstAvailableGestures.SelectedItems.Count == 0)
                return;

            var selectedGestures = lstAvailableGestures.SelectedItems.Cast<GestureItem>().ToList();
            var dialog = new GestureSimilarityTestDialog(selectedGestures)
            {
                Owner = UIHelper.GetParentWindow(this),
            };

            dialog.ShowDialog();
        }

        private void btnMergeGesture_Click(object sender, RoutedEventArgs e)
        {
            MergeGestures();
        }

        private void ImportGestureMenuItem_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofdGestures = new OpenFileDialog()
            {
                Filter = LocalizationProvider.Instance.GetTextValue("Gesture.GestureFile") + "|*" + GestureSign.Common.Constants.GesturesExtension,
                Title = LocalizationProvider.Instance.GetTextValue("Gesture.ImportGesture"),
                CheckFileExists = true
            };
            if (ofdGestures.ShowDialog().GetValueOrDefault())
            {
                var newGestures = GestureManager.LoadGesturesFromFile(ofdGestures.FileName);
                ImportGesture(newGestures);
            }
        }

        private void ImportGesture(List<IGesture> gestureList)
        {
            int count = GestureManager.Instance.ImportGestures(gestureList, null);

            UIHelper.GetParentWindow(this).ShowModalMessageExternal(
                    LocalizationProvider.Instance.GetTextValue("Gesture.Messages.ImportCompleteTitle"),
                    String.Format(LocalizationProvider.Instance.GetTextValue("Gesture.Messages.ImportComplete"),
                        gestureList.Count - count, count), settings: new MetroDialogSettings()
                        {
                            AffirmativeButtonText = LocalizationProvider.Instance.GetTextValue("Common.OK"),
                            ColorScheme = MetroDialogColorScheme.Accented,
                        });
        }

        private void ViewMenuItem_Click(object sender, RoutedEventArgs e)
        {
            MenuItem clickedMenuItem = (MenuItem)sender;
            if (!clickedMenuItem.IsChecked)
                clickedMenuItem.IsChecked = true;

            MenuItem parentMenuItem = clickedMenuItem.Parent as MenuItem;
            if (parentMenuItem != null)
                foreach (var item in parentMenuItem.Items)
                {
                    var current = item as MenuItem;
                    if (!ReferenceEquals(current, clickedMenuItem))
                        if (current != null)
                            current.IsChecked = false;
                }
        }

        private void ListViewItem_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            Dispatcher.InvokeAsync(EditGesture, DispatcherPriority.Input);
        }

        private void GridViewColumnHeaderClickedHandler(object sender, RoutedEventArgs e)
        {
            GridViewColumnHeader headerClicked = e.OriginalSource as GridViewColumnHeader;
            ListSortDirection direction = ListSortDirection.Ascending;

            if (headerClicked != null)
            {
                if (headerClicked.Role != GridViewColumnHeaderRole.Padding)
                {
                    string header = headerClicked.Tag as string;
                    ICollectionView dataView = CollectionViewSource.GetDefaultView(lstAvailableGestures.ItemsSource);
                    if (dataView.SortDescriptions.Count > 1)
                    {
                        direction = dataView.SortDescriptions[0].Direction == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
                        dataView.SortDescriptions.Clear();
                        dataView.SortDescriptions.Add(new SortDescription("PatternCount", direction));
                    }
                    if (header != null)
                    {
                        SortDescription sd = new SortDescription(header, direction);
                        dataView.SortDescriptions.Add(sd);
                    }
                    dataView.Refresh();
                }
            }
        }

        private void EditGesture()
        {
            // Make sure at least one item is selected
            if (lstAvailableGestures.SelectedItems.Count == 0) return;

            var selectedItem = (GestureItem)lstAvailableGestures.SelectedItems[0];
            var gestureId = selectedItem.Gesture?.Id;

            // 尝试作为 contact gesture（Tap/Click/TipTap）打开
            var contactDef = GetRecordedDefinitionById(gestureId);
            GestureDefinition gd;
            if (contactDef != null)
            {
                gd = new GestureDefinition(contactDef);
            }
            else
            {
                gd = new GestureDefinition(
                    GestureManager.Instance.GetNewestGestureSample(selectedItem.Gesture.Name));
            }

            var result = gd.ShowDialog();
            if (result != null && result.Value)
            {
                lstAvailableGestures.SelectedValue = gd.CurrentGesture;
                lstAvailableGestures.Dispatcher.Invoke(DispatcherPriority.Input,
                    new System.Action(() => lstAvailableGestures.ScrollIntoView(lstAvailableGestures.SelectedItem)));
            }
        }

        private static RecordedGestureDefinitionResult GetRecordedDefinitionById(string gestureId)
        {
            if (string.IsNullOrEmpty(gestureId))
                return null;

            var global = ApplicationManager.Instance.GetGlobalApplication()?.ContactGestures;

            var click = global?.Clicks?.FirstOrDefault(c => c.Id == gestureId);
            if (click != null)
                return new RecordedGestureDefinitionResult
                {
                    Type = RecordedGestureType.Click,
                    GestureId = click.Id,
                    Name = click.Name,
                    FingerCount = click.FingerCount,
                    ClickGesture = click,
                };

            var tap = global?.Taps?.FirstOrDefault(t => t.Id == gestureId);
            if (tap != null)
                return new RecordedGestureDefinitionResult
                {
                    Type = RecordedGestureType.Tap,
                    GestureId = tap.Id,
                    Name = tap.Name,
                    FingerCount = tap.FingerCount,
                    TapGesture = tap,
                };

            var tipTap = global?.TipTaps?.FirstOrDefault(t => t.Id == gestureId);
            if (tipTap != null)
                return new RecordedGestureDefinitionResult
                {
                    Type = RecordedGestureType.TipTap,
                    GestureId = tipTap.Id,
                    Name = tipTap.Name,
                    FingerCount = tipTap.FingerCount,
                    TipTapGesture = tipTap,
                };

            return null;
        }

        private void MergeGestures()
        {
            if (lstAvailableGestures.SelectedItems.Count < 2)
                return;

            var selectedGestures = lstAvailableGestures.SelectedItems.Cast<GestureItem>()
                .Where(item => item?.Gesture != null)
                .ToList();
            if (selectedGestures.Count < 2)
                return;

            var dialog = new MergeGesturesDialog(selectedGestures)
            {
                Owner = UIHelper.GetParentWindow(this),
            };

            var result = dialog.ShowDialog();
            if (!result.GetValueOrDefault() || dialog.TargetGesture?.Gesture == null)
                return;

            var targetGesture = dialog.TargetGesture.Gesture;
            var sourceGestures = selectedGestures
                .Where(item => item.Gesture != null && !ReferenceEquals(item.Gesture, targetGesture) && item.Gesture.Id != targetGesture.Id)
                .ToList();
            if (sourceGestures.Count == 0)
                return;

            foreach (var sourceGesture in sourceGestures.Select(item => item.Gesture))
            {
                ApplicationManager.Instance.Applications.RebindGestures(
                    sourceGesture.Id,
                    targetGesture.Id,
                    sourceGesture.Name,
                    targetGesture.Name);

                if (!string.IsNullOrEmpty(sourceGesture.Id))
                    GestureManager.Instance.DeleteGestureById(sourceGesture.Id);
                else if (!string.IsNullOrEmpty(sourceGesture.Name))
                    GestureManager.Instance.DeleteGesture(sourceGesture.Name);
            }

            ApplicationManager.Instance.SaveApplications();
            GestureManager.Instance.SaveGestures();

            lstAvailableGestures.Dispatcher.InvokeAsync(() => ReselectGesture(targetGesture.Id, targetGesture.Name), DispatcherPriority.Input);

            var parentWindow = UIHelper.GetParentWindow(this);
            parentWindow?.ShowModalMessageExternal(
                LocalizationProvider.Instance.GetTextValue("Gesture.Messages.MergeCompleteTitle"),
                string.Format(LocalizationProvider.Instance.GetTextValue("Gesture.Messages.MergeComplete"), sourceGestures.Count, targetGesture.Name));
        }

        private void ReselectGesture(string targetGestureId, string targetGestureName)
        {
            GestureItem selectedItem = null;
            foreach (var item in lstAvailableGestures.Items.OfType<GestureItem>())
            {
                if (item?.Gesture == null)
                    continue;

                bool idMatch = !string.IsNullOrEmpty(targetGestureId) && string.Equals(item.Gesture.Id, targetGestureId, StringComparison.Ordinal);
                bool nameMatch = string.Equals(item.Gesture.Name, targetGestureName, StringComparison.Ordinal);
                if (idMatch || nameMatch)
                {
                    selectedItem = item;
                    break;
                }
            }

            if (selectedItem == null)
                return;

            lstAvailableGestures.SelectedItem = selectedItem;
            lstAvailableGestures.ScrollIntoView(selectedItem);
        }

        protected override void OnDrop(DragEventArgs e)
        {
            base.OnDrop(e);

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                try
                {
                    string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    foreach (var file in files)
                    {
                        if (file.EndsWith(GestureSign.Common.Constants.GesturesExtension, StringComparison.OrdinalIgnoreCase))
                        {
                            var newGestures = GestureManager.LoadGesturesFromFile(file);
                            ImportGesture(newGestures);
                        }
                    }
                }
                catch (Exception exception)
                {
                    UIHelper.GetParentWindow(this).ShowModalMessageExternal(exception.GetType().Name, exception.Message);
                }
            }
            e.Handled = true;
        }
    }
}
