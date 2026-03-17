using GestureSign.Common.Applications;
using GestureSign.Common.Gestures;
using System.Collections.Generic;
using System.Linq;

namespace GestureSign.Common.Extensions
{
    public static class ApplicationExtensions
    {
        public static List<IGesture> GetRelatedGestures(this IEnumerable<IApplication> applications, IEnumerable<IGesture> gestures)
        {
            var result = new List<IGesture>();
            foreach (var app in applications)
            {
                if (app.Actions == null) continue;
                foreach (var action in app.Actions)
                {
                    if (action == null) continue;
                    IGesture gesture = !string.IsNullOrEmpty(action.GestureId)
                        ? gestures.FirstOrDefault(g => g.Id == action.GestureId)
                        : gestures.FirstOrDefault(g => g.Name == action.GestureName);
                    if (gesture != null && !result.Contains(gesture))
                        result.Add(gesture);
                }
            }
            return result;
        }

        public static void RenameGestures(this IEnumerable<IApplication> applications, string oldName, string newName)
        {
            foreach (var app in applications)
            {
                if (app.Actions == null) continue;
                foreach (var action in app.Actions)
                {
                    if (action.GestureName == oldName)
                        action.GestureName = newName;
                }
            }
        }

        public static void RebindGestures(this IEnumerable<IApplication> applications, string oldId, string newId, string oldName, string newName)
        {
            foreach (var app in applications)
            {
                if (app.Actions == null) continue;
                foreach (var action in app.Actions)
                {
                    bool idMatch = !string.IsNullOrEmpty(oldId) && action.GestureId == oldId;
                    bool legacyNameMatch = string.IsNullOrEmpty(action.GestureId) && action.GestureName == oldName;
                    if (idMatch || legacyNameMatch)
                    {
                        action.GestureId = newId;
                        action.GestureName = newName;
                    }
                }
            }
        }
    }
}
