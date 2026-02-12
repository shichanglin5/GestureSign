using System;
using System.Data;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using GestureSign.Common.Gestures;
using GestureSign.Common.Localization;
using GestureSign.Common.Log;
using GestureSign.ControlPanel.ViewModel;
using MahApps.Metro.Controls;
using ManagedWinapi;
using ManagedWinapi.Hooks;
using System.Linq;
using GestureSign.Common.Input;

namespace GestureSign.ControlPanel.Dialogs
{
    /// <summary>
    /// Interaction logic for ActionDialog.xaml
    /// </summary>
    public partial class ActionDialog : MetroWindow
    {
        #region Private Instance Fields

        private readonly IAction _sourceAction;
        private IApplication _sourceApplication;

        #endregion

        #region Public Instance Fields

        public IAction NewAction { get; private set; } = new GestureSign.Common.Applications.Action();

        #endregion

        #region Dependency Properties

        public IGesture CurrentGesture
        {
            get { return (IGesture)GetValue(CurrentGestureProperty); }
            set { SetValue(CurrentGestureProperty, value); }
        }

        public static readonly DependencyProperty CurrentGestureProperty =
            DependencyProperty.Register(nameof(CurrentGesture), typeof(IGesture), typeof(ActionDialog), new PropertyMetadata(new Gesture()));

        #endregion

        #region Constructors

        protected ActionDialog()
        {
            InitializeComponent();
        }

        public ActionDialog(IAction sourceAction, IApplication sourceApplication) : this()
        {
            _sourceAction = sourceAction;
            _sourceApplication = sourceApplication;
        }

        #endregion

        #region Events

        private void MetroWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_sourceAction != null)
            {
                ActionNameTextBox.Text = _sourceAction.Name;
                ConditionTextBox.Text = _sourceAction.Condition;
                ActivateWindowCheckBox.IsChecked = _sourceAction.ActivateWindow;
                TouchScreenCheckBox.IsChecked = !_sourceAction.IgnoredDevices.HasFlag(Devices.TouchScreen);
                TouchPadCheckBox.IsChecked = !_sourceAction.IgnoredDevices.HasFlag(Devices.TouchPad);

                var gesture = GestureManager.Instance.GetNewestGestureSample(_sourceAction.GestureName);
                if (gesture != null)
                    CurrentGesture = gesture;

                var hotkey = _sourceAction.Hotkey;
                if (hotkey != null)
                    HotKeyTextBox.HotKey = new HotKey(KeyInterop.KeyFromVirtualKey(hotkey.KeyCode), (ModifierKeys)hotkey.ModifierKeys);
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // Save gesture if present (always save to ensure thumbnail update)
            if (CurrentGesture != null && CurrentGesture.PointPatterns != null)
            {
                SaveGesture(CurrentGesture);
            }

            if (SaveAction())
            {
                if (!DialogResult.GetValueOrDefault())
                    DialogResult = true;
                Close();
            }
        }

        private void ResetHotKeyButton_Click(object sender, RoutedEventArgs e)
        {
            HotKeyTextBox.HotKey = null;
        }

        private void ConditionTextBox_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            EditConditionDialog editConditionDialog = new EditConditionDialog(ConditionTextBox.Text);
            if (editConditionDialog.ShowDialog().Value)
            {
                ConditionTextBox.Text = editConditionDialog.ConditionTextBox.Text;
            }
        }

        #endregion

        #region Private Methods

        private bool ShowErrorMessage(string title, string message)
        {
            MessageFlyoutText.Text = message;
            MessageFlyout.Header = title;
            MessageFlyout.IsOpen = true;
            return false;
        }

        private bool SaveAction()
        {
            try
            {
                var regex = new Regex("finger_[0-9]+_((start|end)_[XY]%?|ID)");
                var replaced = regex.Replace(ConditionTextBox.Text, "10");

                DataTable dataTable = new DataTable();
                dataTable.Compute(replaced, null);
            }
            catch (Exception exception)
            {
                return ShowErrorMessage(LocalizationProvider.Instance.GetTextValue("ActionDialog.Messages.ConditionError"), exception.Message);
            }

            // 直接使用 Contains 检查 _sourceAction 是否在列表中
            if (_sourceApplication.Actions.Contains(_sourceAction))
            {
                NewAction = _sourceAction;
            }
            else
            {
                _sourceApplication.AddAction(NewAction);
            }

            // Store new values
            NewAction.Condition = string.IsNullOrWhiteSpace(ConditionTextBox.Text) ? null : ConditionTextBox.Text;
            NewAction.ActivateWindow = ActivateWindowCheckBox.IsChecked;
            NewAction.Name = ActionNameTextBox.Text.Trim();
            NewAction.Hotkey = HotKeyTextBox.HotKey != null
                ? new Hotkey()
                {
                    KeyCode = KeyInterop.VirtualKeyFromKey(HotKeyTextBox.HotKey.Key),
                    ModifierKeys = (int)HotKeyTextBox.HotKey.ModifierKeys
                }
                : null;

            NewAction.GestureName = CurrentGesture?.Name ?? string.Empty;

            Devices ignoredDevices = Devices.None;
            if (!TouchScreenCheckBox.IsChecked.GetValueOrDefault())
                ignoredDevices |= Devices.TouchScreen;
            if (!TouchPadCheckBox.IsChecked.GetValueOrDefault())
                ignoredDevices |= Devices.TouchPad;
            NewAction.IgnoredDevices = ignoredDevices;

            ApplicationManager.Instance.SaveApplications();

            return true;
        }

        private bool SaveGesture(IGesture gesture)
        {
            if (string.IsNullOrEmpty(gesture.Name))
            {
                gesture.Name = GestureManager.Instance.GetNewGestureName();
            }

            if (GestureManager.Instance.GestureExists(gesture.Name))
            {
                GestureManager.Instance.DeleteGesture(gesture.Name);
            }

            GestureManager.Instance.AddGesture(gesture);
            GestureManager.Instance.SaveGestures();

            return true;
        }

        #endregion
    }
}
