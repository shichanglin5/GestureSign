using System.ComponentModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace GestureSign.Common.Applications
{
    /// <summary>
    /// 匹配条件类型
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum MatchConditionType
    {
        /// <summary>
        /// 窗口类名
        /// </summary>
        ClassName = 0,

        /// <summary>
        /// 窗口标题
        /// </summary>
        Title = 1,

        /// <summary>
        /// 进程名（如 chrome.exe）
        /// </summary>
        ProcessName = 2,

        /// <summary>
        /// 完整进程路径
        /// </summary>
        ProcessPath = 3,

        /// <summary>
        /// 应用 ID（UWP/PWA）
        /// </summary>
        AUMID = 4,

        /// <summary>
        /// 焦点控件是否为文本输入框
        /// </summary>
        FocusedTextInput = 5
    }

    /// <summary>
    /// 单个匹配条件
    /// </summary>
    public class MatchCondition
    {
        /// <summary>
        /// 条件类型
        /// </summary>
        public MatchConditionType Type { get; set; }

        /// <summary>
        /// 匹配值
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// 是否使用正则表达式（仅 Title 类型有效）
        /// </summary>
        [DefaultValue(false)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool IsRegex { get; set; }

        /// <summary>
        /// 获取条件的性能优先级（数字越小越快）
        /// </summary>
        public int GetPriority()
        {
            return Type switch
            {
                MatchConditionType.ClassName => 0,    // 最快
                MatchConditionType.Title => 1,
                MatchConditionType.ProcessName => 2,
                MatchConditionType.ProcessPath => 3,
                MatchConditionType.AUMID => 4,
                MatchConditionType.FocusedTextInput => 5, // 需要跨线程检测
                _ => 99
            };
        }

        public override string ToString()
        {
            return IsRegex ? $"{Type}: /{Value}/" : $"{Type}: {Value}";
        }
    }
}
