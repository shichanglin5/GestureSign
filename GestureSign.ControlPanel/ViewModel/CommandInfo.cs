using GestureSign.Common.Applications;
using GestureSign.Common.Localization;
using GestureSign.Common.Plugins;
using GestureSign.Common.Gestures;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GestureSign.ControlPanel.ViewModel
{
    public class CommandInfo : INotifyPropertyChanged, IEquatable<IAction>, IComparable, IComparable<CommandInfo>
    {

        public CommandInfo(IAction action, ICommand command, string commandName, string description, bool isEnabled)
        {
            Action = action;
            Command = command;
            IsEnabled = isEnabled;
            CommandName = commandName;
            Description = description;

            // 订阅 Command 的属性变更事件
            if (command is INotifyPropertyChanged notifyCommand)
            {
                notifyCommand.PropertyChanged += Command_PropertyChanged;
            }
        }

        private void Command_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ICommand.CommandSettings) ||
                e.PropertyName == nameof(ICommand.PluginClass) ||
                e.PropertyName == nameof(ICommand.PluginFilename))
            {
                UpdateDescriptionAndName();
            }
        }

        private void UpdateDescriptionAndName()
        {
            if (string.IsNullOrEmpty(Command.PluginClass) || string.IsNullOrEmpty(Command.PluginFilename))
            {
                Description = LocalizationProvider.Instance.GetTextValue("Action.Messages.DoubleClickToEditCommand");
            }
            else if (PluginManager.Instance.PluginExists(Command.PluginClass, Command.PluginFilename))
            {
                try
                {
                    var pluginInfo = PluginManager.Instance.FindPluginByClassAndFilename(
                        Command.PluginClass, Command.PluginFilename);
                    pluginInfo.Plugin.Deserialize(Command.CommandSettings);
                    CommandName = pluginInfo.Plugin.Name;
                    Description = pluginInfo.Plugin.Description;
                }
                catch
                {
                    Description = LocalizationProvider.Instance.GetTextValue("Action.Messages.DoubleClickToEditCommand");
                }
            }
            else
            {
                Description = string.Format(LocalizationProvider.Instance.GetTextValue("Action.Messages.NoAssociationAction"),
                    Command.PluginClass, Command.PluginFilename);
            }
        }

        private bool _isEnabled;
        private IAction _action;

        public bool IsEnabled
        {
            get
            {
                return _isEnabled;
            }

            set { SetProperty(ref _isEnabled, value); }
        }

        private string _commandName;
        public string CommandName
        {
            get => _commandName;
            set => SetProperty(ref _commandName, value);
        }

        private string _description;
        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        public IAction Action
        {
            get { return _action; }
            set { SetProperty(ref _action, value); }
        }

        public string ActionName
        {
            get
            {
                return Action?.Name;
            }
        }

        public string GestureFeatures { get; set; }

        public int PatternCount { get; set; }

        public ICommand Command { get; set; }

        private int _order;
        /// <summary>
        /// Order index for sorting within the same action
        /// This ensures stable sorting when ListCollectionView applies SortDescriptions
        /// </summary>
        public int Order
        {
            get { return _order; }
            set { SetProperty(ref _order, value); }
        }

        public int FingerCount
        {
            get
            {
                if (Action == null)
                    return 0;

                var gesture = !string.IsNullOrEmpty(Action.GestureId)
                    ? GestureManager.Instance.GetGestureById(Action.GestureId)
                    : null;

                gesture ??= string.IsNullOrEmpty(Action.GestureName)
                    ? null
                    : GestureManager.Instance.GetNewestGestureSample(Action.GestureName);

                return gesture?.FingerCount ?? 0;
            }
        }

        public string FingerCountText
        {
            get
            {
                var fingerCount = FingerCount;
                return fingerCount > 0 ? $"{fingerCount}指" : "";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            if (this.PropertyChanged != null)
            {
                this.PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }
        protected void SetProperty<T>(ref T storage, T value, [CallerMemberName] String propertyName = null)
        {
            if (Equals(storage, value)) return;

            storage = value;
            this.OnPropertyChanged(propertyName);
        }

        public int CompareTo(object obj)
        {
            var info = (CommandInfo)obj;
            return CompareTo(info);
        }

        public int CompareTo(CommandInfo info)
        {
            if (info.Action == Action)
            {
                if (Action.Commands == null) return 0;
                foreach (var command in Action.Commands)
                {
                    if (command == Command)
                        return -1;
                    else if (command == info.Command)
                        return 1;
                }
                return 0;
            }
            else
            {
                return 0;
            }
        }

        public bool Equals(IAction action)
        {
            return action != null && CommandName == action.Name;
        }

        public static CommandInfo FromCommand(ICommand command, IAction action)
        {
            string description;
            string pluginName = string.Empty;
            // Ensure this action has a plugin
            if (string.IsNullOrEmpty(command.PluginClass) || string.IsNullOrEmpty(command.PluginFilename))
            {
                description = LocalizationProvider.Instance.GetTextValue("Action.Messages.DoubleClickToEditCommand");
            }
            else if (PluginManager.Instance.PluginExists(command.PluginClass, command.PluginFilename))
            {
                try
                {
                    // Get plugin for this action
                    IPluginInfo pluginInfo =
                        PluginManager.Instance.FindPluginByClassAndFilename(command.PluginClass,
                            command.PluginFilename);

                    // Feed settings to plugin
                    if (!pluginInfo.Plugin.Deserialize(command.CommandSettings))
                        command.CommandSettings = pluginInfo.Plugin.Serialize();

                    pluginName = pluginInfo.Plugin.Name;
                    description = pluginInfo.Plugin.Description;
                }
                catch
                {
                    pluginName = string.Empty;
                    description = LocalizationProvider.Instance.GetTextValue("Action.Messages.DoubleClickToEditCommand");
                }
            }
            else
            {
                description = string.Format(LocalizationProvider.Instance.GetTextValue("Action.Messages.NoAssociationAction"), command.PluginClass, command.PluginFilename);
            }

            return new CommandInfo(action, command, pluginName, description, command.IsEnabled);
        }
    }
}
