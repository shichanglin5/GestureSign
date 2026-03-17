using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using GestureSign.Common.Gestures;
using GestureSign.Common.Input;
using GestureSign.Common.Log;
using GestureSign.ControlPanel.Common;

namespace GestureSign.ControlPanel.ViewModel
{
    public class GestureItemProvider : INotifyPropertyChanged
    {
        private static ObservableCollection<GestureItem> _gestureItems;
        private static Dictionary<string, GestureItem> _gestureMap = new Dictionary<string, GestureItem>();

        // 静态属性变化事件
        public static event PropertyChangedEventHandler StaticPropertyChanged;

        private static event EventHandler<string> GlobalPropertyChanged;

        public event PropertyChangedEventHandler PropertyChanged;

        static GestureItemProvider()
        {
            _gestureItems = new ObservableCollection<GestureItem>();

            GestureManager.GestureSaved += (o, e) =>
            {
                Application.Current.Dispatcher.Invoke(Update);
            };

            AppConfig.ConfigChanged += (o, e) =>
            {
                Application.Current.Dispatcher.Invoke(Update);
            };

            ApplicationManager.Instance.LoadingTask.ContinueWith((task) =>
            {
                ApplicationManager.ApplicationSaved += (o, e) =>
                {
                    Application.Current.Dispatcher.Invoke(Update);
                };
                GestureManager.Instance.LoadingTask.Wait();
                Application.Current.Dispatcher.Invoke(Update);
            });
        }

        public GestureItemProvider()
        {
            GlobalPropertyChanged += (sender, propertyName) =>
            {
                OnPropertyChanged(propertyName);
            };
        }

        public static ObservableCollection<GestureItem> GestureItems
        {
            get { return _gestureItems; }
            set { _gestureItems = value; }
        }

        public static Dictionary<string, GestureItem> GestureMap
        {
            get { return _gestureMap; }
            private set
            {
                if (_gestureMap != value)
                {
                    _gestureMap = value;
                    StaticPropertyChanged?.Invoke(null, new PropertyChangedEventArgs(nameof(GestureMap)));
                }
            }
        }

        public Dictionary<string, GestureItem> InstanceGestureMap
        {
            get
            {
                return new Dictionary<string, GestureItem>(GestureMap);
            }
        }

        protected virtual void OnPropertyChanged(string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private static void Update()
        {
            if (_gestureItems == null)
                _gestureItems = new ObservableCollection<GestureItem>();
            else
                _gestureItems.Clear();

            var apps = ApplicationManager.Instance.Applications.Where(app => !(app is IgnoredApp)).ToList();
            var brush = (SolidColorBrush)Application.Current.Resources["MahApps.Brushes.Highlight"];
            var color = brush.Color;

            foreach (var g in GestureManager.Instance.Gestures)
            {
                var gesture = (Gesture)g;
                string result = string.Empty;
                foreach (IApplication application in apps)
                {
                    if (application.Actions.Any(a => (!string.IsNullOrEmpty(a.GestureId) && a.GestureId == gesture.Id) || a.GestureName == gesture.Name))
                        result += $" {application.Name},";
                }
                result = result.TrimEnd(',');

                GestureItem newItem = new GestureItem()
                {
                    GestureImage = GestureImage.CreateImage(gesture.PointPatterns, new Size(60, 60), color),
                    Features = GestureManager.Instance.GetNewGestureId(gesture.PointPatterns),
                    PatternCount = gesture?.PointPatterns.Max(p => p.Points.Length) ?? 0,
                    Applications = result,
                    Gesture = gesture
                };
                GestureItems.Add(newItem);
            }

            var gestureMap = new Dictionary<string, GestureItem>(StringComparer.Ordinal);
            foreach (var item in GestureItems)
            {
                if (!string.IsNullOrEmpty(item.Gesture?.Id) && !gestureMap.ContainsKey(item.Gesture.Id))
                    gestureMap[item.Gesture.Id] = item;

                if (!string.IsNullOrEmpty(item.Gesture?.Name) && !gestureMap.ContainsKey(item.Gesture.Name))
                    gestureMap[item.Gesture.Name] = item;
            }

            // ContactGesture（Tap/TipTap/Click）不在 GestureManager 中，需要单独生成图像
            AddContactGestureImages(gestureMap, color);

            GestureMap = gestureMap;
            GlobalPropertyChanged?.Invoke(typeof(GestureItemProvider), nameof(InstanceGestureMap));
        }

        /// <summary>
        /// 用 ContactGestureDisplayFactory 创建带 StrokeStyles 的显示手势，替换或新增到 gestureMap。
        /// 因为 contact gesture 的显示 Gesture 可能已被 SaveGesture 保存到 GestureManager（Gestures.json），
        /// 反序列化后 StrokeStyles 丢失，所以需要用工厂重新生成的版本替换。
        /// </summary>
        private static void AddOrReplaceContactGestureItem(
            Dictionary<string, GestureItem> gestureMap,
            RecordedGestureDefinitionResult definition,
            string id, string name, Color color)
        {
            var displayGesture = ContactGestureDisplayFactory.CreateDisplayGesture(definition);
            if (displayGesture?.PointPatterns == null)
                return;

            var item = new GestureItem
            {
                GestureImage = GestureImage.CreateImage(displayGesture.PointPatterns, new Size(60, 60), color),
                Gesture = displayGesture,
            };

            // 如果 GestureManager 中已有同 Id 的条目（从 Gestures.json 加载的旧副本），替换它
            if (gestureMap.TryGetValue(id, out var existingItem))
            {
                int index = GestureItems.IndexOf(existingItem);
                if (index >= 0)
                    GestureItems[index] = item;
                else
                    GestureItems.Add(item);
            }
            else
            {
                GestureItems.Add(item);
            }

            gestureMap[id] = item;
            if (!string.IsNullOrEmpty(name))
                gestureMap[name] = item;
        }

        private static void AddContactGestureImages(Dictionary<string, GestureItem> gestureMap, Color color)
        {
            var globalApp = ApplicationManager.Instance.GetGlobalApplication();
            var contactGestures = globalApp?.ContactGestures;
            if (contactGestures == null)
                return;

            foreach (var tap in contactGestures.Taps)
            {
                if (string.IsNullOrEmpty(tap.Id))
                    continue;

                var definition = new RecordedGestureDefinitionResult
                {
                    Type = RecordedGestureType.Tap,
                    FingerCount = tap.FingerCount,
                    GestureId = tap.Id,
                    Name = tap.Name,
                    TapGesture = tap,
                };
                AddOrReplaceContactGestureItem(gestureMap, definition, tap.Id, tap.Name, color);
            }

            foreach (var click in contactGestures.Clicks)
            {
                if (string.IsNullOrEmpty(click.Id))
                    continue;

                var definition = new RecordedGestureDefinitionResult
                {
                    Type = RecordedGestureType.Click,
                    FingerCount = click.FingerCount,
                    GestureId = click.Id,
                    Name = click.Name,
                    ClickGesture = click,
                };
                AddOrReplaceContactGestureItem(gestureMap, definition, click.Id, click.Name, color);
            }

            foreach (var tipTap in contactGestures.TipTaps)
            {
                if (string.IsNullOrEmpty(tipTap.Id))
                    continue;

                var definition = new RecordedGestureDefinitionResult
                {
                    Type = RecordedGestureType.TipTap,
                    FingerCount = tipTap.FingerCount,
                    GestureId = tipTap.Id,
                    Name = tipTap.Name,
                    TipTapGesture = tipTap,
                };
                AddOrReplaceContactGestureItem(gestureMap, definition, tipTap.Id, tipTap.Name, color);
            }
        }
    }
}
