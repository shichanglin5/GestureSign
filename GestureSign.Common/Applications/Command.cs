using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Converters;

namespace GestureSign.Common.Applications
{
    public class Command : ICommand, ICloneable, INotifyPropertyChanged
    {
        #region Private Fields

        private string _commandSettings;
        private string _pluginClass;
        private string _pluginFilename;
        private bool _isEnabled = true;

        #endregion

        #region Public Properties

        public string CommandSettings
        {
            get => _commandSettings;
            set => SetProperty(ref _commandSettings, value);
        }

        public string PluginClass
        {
            get => _pluginClass;
            set => SetProperty(ref _pluginClass, value);
        }

        public string PluginFilename
        {
            get => _pluginFilename;
            set => SetProperty(ref _pluginFilename, value);
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        #endregion

        #region INotifyPropertyChanged Implementation

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(storage, value))
                return false;

            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion

        #region ICloneable Implementation

        public object Clone()
        {
            // MemberwiseClone creates a shallow copy
            // Event subscribers are NOT copied (this is correct behavior)
            return MemberwiseClone();
        }

        #endregion
    }

    public class CommandConverter : CustomCreationConverter<ICommand>
    {
        public override ICommand Create(Type objectType)
        {
            return new Command();
        }
    }
}
