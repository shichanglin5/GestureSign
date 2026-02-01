using ManagedWinapi.Windows;
using Newtonsoft.Json;

namespace GestureSign.Common.Applications
{
    /// <summary>
    /// 窗口匹配规则接口
    /// </summary>
    public interface IWindowRule
    {
        /// <summary>
        /// 判断窗口是否匹配此规则
        /// </summary>
        bool IsMatch(SystemWindow window);

        /// <summary>
        /// 判断窗口是否匹配此规则（使用缓存）
        /// </summary>
        bool IsMatch(WindowInfoCache windowInfo);

        /// <summary>
        /// 获取显示名称（用于列表展示）
        /// </summary>
        string GetDisplayName();

        /// <summary>
        /// 显示名称属性（用于 WPF 绑定）
        /// </summary>
        [JsonIgnore]
        string DisplayName { get; }
    }
}
