using GestureSign.Common.Applications;
using GestureSign.Common.Extensions;
using GestureSign.Common.Gestures;
using GestureSign.Common.Input;
using GestureSign.Common.Localization;
using GestureSign.ControlPanel.Common;
using MahApps.Metro.Controls;
using System.Linq;
using System.Windows;

namespace GestureSign.ControlPanel.Dialogs
{
    /// <summary>
    /// GestureDefinition.xaml 的交互逻辑
    /// </summary>
    public partial class GestureDefinition : MetroWindow
    {
        private Gesture _oldGesture;

        public IGesture CurrentGesture
        {
            get { return (IGesture)GetValue(CurrentGestureProperty); }
            set { SetValue(CurrentGestureProperty, value); }
        }

        public static readonly DependencyProperty CurrentGestureProperty =
            DependencyProperty.Register(nameof(CurrentGesture), typeof(IGesture), typeof(GestureDefinition), new PropertyMetadata(new Gesture()));

        public RecordedGestureDefinitionResult CurrentRecordedDefinition
        {
            get { return (RecordedGestureDefinitionResult)GetValue(CurrentRecordedDefinitionProperty); }
            set { SetValue(CurrentRecordedDefinitionProperty, value); }
        }

        public static readonly DependencyProperty CurrentRecordedDefinitionProperty =
            DependencyProperty.Register(nameof(CurrentRecordedDefinition), typeof(RecordedGestureDefinitionResult), typeof(GestureDefinition), new PropertyMetadata(null));

        protected GestureDefinition()
        {
            InitializeComponent();
        }

        public GestureDefinition(IGesture gesture)
            : this()
        {
            CurrentGesture = _oldGesture = (Gesture)gesture;
            GestureSelector.OldGesture = _oldGesture;
        }

        public GestureDefinition(RecordedGestureDefinitionResult definition)
            : this()
        {
            CurrentRecordedDefinition = definition;
            var displayGesture = ContactGestureDisplayFactory.CreateDisplayGesture(definition);
            if (displayGesture != null)
            {
                CurrentGesture = displayGesture;
                _oldGesture = (Gesture)displayGesture;
                GestureSelector.OldGesture = _oldGesture;
            }
        }

        private void cmdDone_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentGesture == null && CurrentRecordedDefinition == null)
            {
                return;
            }

            if (CurrentRecordedDefinition != null)
            {
                ContactGestureDisplayFactory.EnsureIdentity(CurrentRecordedDefinition);

                if (_oldGesture != null)
                {
                    if (!string.IsNullOrEmpty(_oldGesture.Id) &&
                        !string.IsNullOrEmpty(CurrentRecordedDefinition.GestureId) &&
                        _oldGesture.Id != CurrentRecordedDefinition.GestureId)
                    {
                        int referenceCount = ApplicationManager.Instance.Applications
                            .Where(app => app.Actions != null)
                            .SelectMany(app => app.Actions)
                            .Count(action => action != null && action.GestureId == _oldGesture.Id);

                        var result = MessageBox.Show(
                            string.Format(LocalizationProvider.Instance.GetTextValue("GestureDefinition.GlobalGestureEditWarning"), referenceCount),
                            LocalizationProvider.Instance.GetTextValue("GestureDefinition.GlobalGestureEditWarningTitle"),
                            MessageBoxButton.OKCancel,
                            MessageBoxImage.Warning);

                        if (result != MessageBoxResult.OK)
                        {
                            return;
                        }

                        GestureManager.Instance.DeleteGestureById(_oldGesture.Id);
                        ApplicationManager.Instance.Applications.RebindGestures(_oldGesture.Id, CurrentRecordedDefinition.GestureId, _oldGesture.Name, CurrentRecordedDefinition.Name);
                        ApplicationManager.Instance.SaveApplications();
                    }
                }

                if (SaveRecordedDefinition(CurrentRecordedDefinition))
                {
                    if (!DialogResult.GetValueOrDefault())
                        DialogResult = true;
                    Close();
                }

                return;
            }

            if (_oldGesture != null)
            {
                if (!string.IsNullOrEmpty(_oldGesture.Id) && !string.IsNullOrEmpty(CurrentGesture.Id) && _oldGesture.Id != CurrentGesture.Id)
                {
                    int referenceCount = ApplicationManager.Instance.Applications
                        .Where(app => app.Actions != null)
                        .SelectMany(app => app.Actions)
                        .Count(action => action != null && action.GestureId == _oldGesture.Id);

                    var result = MessageBox.Show(
                        string.Format(LocalizationProvider.Instance.GetTextValue("GestureDefinition.GlobalGestureEditWarning"), referenceCount),
                        LocalizationProvider.Instance.GetTextValue("GestureDefinition.GlobalGestureEditWarningTitle"),
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Warning);

                    if (result != MessageBoxResult.OK)
                    {
                        return;
                    }

                    GestureManager.Instance.DeleteGestureById(_oldGesture.Id);
                    ApplicationManager.Instance.Applications.RebindGestures(_oldGesture.Id, CurrentGesture.Id, _oldGesture.Name, CurrentGesture.Name);
                    ApplicationManager.Instance.SaveApplications();
                }
                else
                {
                    CurrentGesture.Id = _oldGesture.Id;
                }
            }

            bool sameGestureIdRename = _oldGesture != null &&
                !string.IsNullOrEmpty(_oldGesture.Id) &&
                _oldGesture.Id == CurrentGesture.Id &&
                !string.Equals(_oldGesture.Name, CurrentGesture.Name);

            if (SaveGesture(CurrentGesture))
            {
                if (sameGestureIdRename)
                {
                    foreach (var app in ApplicationManager.Instance.Applications)
                    {
                        if (app.Actions == null) continue;
                        foreach (var action in app.Actions)
                        {
                            if (action?.GestureId == CurrentGesture.Id)
                                action.GestureName = CurrentGesture.Name;
                        }
                    }

                    ApplicationManager.Instance.SaveApplications();
                }

                if (!DialogResult.GetValueOrDefault())
                    DialogResult = true;
                Close();
            }
        }

        #region Private Methods

        private bool SaveGesture(IGesture gesture)
        {
            if (string.IsNullOrEmpty(gesture.Id) && gesture.PointPatterns != null)
            {
                gesture.Id = GestureManager.Instance.GetNewGestureId(gesture.PointPatterns);
            }

            if (string.IsNullOrEmpty(gesture.Name))
            {
                gesture.Name = GestureManager.Instance.GetNewGestureName();
            }

            if (!string.IsNullOrEmpty(gesture.Id))
            {
                GestureManager.Instance.DeleteGestureById(gesture.Id);
            }
            GestureManager.Instance.AddGesture(gesture);

            GestureManager.Instance.SaveGestures();

            return true;
        }

        private bool SaveRecordedDefinition(RecordedGestureDefinitionResult definition)
        {
            if (!ContactGestureDisplayFactory.SaveToGlobalApp(definition))
                return false;

            ApplicationManager.Instance.SaveApplications();
            return true;
        }

        #endregion
    }
}
