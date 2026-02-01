using ManagedWinapi.Windows;
using Newtonsoft.Json;

namespace GestureSign.Common.Applications
{
    /// <summary>
    /// 窗口规则引用（引用预置窗口规则）
    /// 只包含预置规则 ID，匹配时委托给预置规则
    /// </summary>
    public class WindowRuleRef : IWindowRule
    {
        /// <summary>
        /// 引用的预置规则 ID
        /// </summary>
        public string PresetId { get; set; }

        /// <summary>
        /// 获取引用的预置规则
        /// </summary>
        private WindowRule GetReferencedPreset()
        {
            if (!string.IsNullOrEmpty(PresetId))
            {
                return WindowPresetManager.Instance.GetPresetById(PresetId);
            }
            return null;
        }

        /// <summary>
        /// 判断窗口是否匹配（委托给预置规则）
        /// </summary>
        public bool IsMatch(SystemWindow window)
        {
            var preset = GetReferencedPreset();
            return preset?.IsMatch(window) ?? false;
        }

        /// <summary>
        /// 判断窗口是否匹配（委托给预置规则）
        /// </summary>
        public bool IsMatch(WindowInfoCache windowInfo)
        {
            var preset = GetReferencedPreset();
            return preset?.IsMatch(windowInfo) ?? false;
        }

        /// <summary>
        /// 获取显示名称
        /// </summary>
        public string GetDisplayName()
        {
            var preset = GetReferencedPreset();
            var name = preset?.Name ?? PresetId ?? "(未知)";
            return $"[预置] {name}";
        }

        /// <summary>
        /// 显示名称属性（用于 WPF 绑定）
        /// </summary>
        [JsonIgnore]
        public string DisplayName => GetDisplayName();
    }
}
