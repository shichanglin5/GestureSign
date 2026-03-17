using GestureSign.Common.Gestures;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace GestureSign.Common.Applications
{
    /// <summary>
    /// 应用级连续手势设置（包含继承策略和配置列表）
    /// </summary>
    [JsonConverter(typeof(ContinuousGestureSettingsConverter))]
    public class ContinuousGestureSettings
    {
        /// <summary>
        /// 继承策略位掩码，每个 bit 对应一个手指数：
        /// bit 0 = 2指, bit 1 = 3指, bit 2 = 4指
        /// 默认全部继承（0b111 = 7）
        /// </summary>
        [DefaultValue(0b111)]
        public int InheritBits { get; set; } = 0b111;

        /// <summary>
        /// 配置列表
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public List<ContinuousGestureConfig> Configs { get; set; } = new List<ContinuousGestureConfig>();

        /// <summary>
        /// 检查指定手指数是否继承全局配置
        /// </summary>
        public bool IsInherited(int fingerCount)
        {
            int bit = fingerCount - 2; // 2指=bit0, 3指=bit1, 4指=bit2
            if (bit < 0 || bit > 2) return false;
            return (InheritBits & (1 << bit)) != 0;
        }

        /// <summary>
        /// 设置指定手指数的继承状态
        /// </summary>
        public void SetInherited(int fingerCount, bool inherit)
        {
            int bit = fingerCount - 2;
            if (bit < 0 || bit > 2) return;
            if (inherit)
                InheritBits |= (1 << bit);
            else
                InheritBits &= ~(1 << bit);
        }

        /// <summary>
        /// 判断是否为空（无配置且全部继承 — 即默认状态）
        /// 用于 JSON 序列化时跳过空值
        /// </summary>
        public bool ShouldSerialize()
        {
            return Configs.Count > 0 || InheritBits != 0b111;
        }
    }

    /// <summary>
    /// 单条连续手势配置（存储在 IApplication 级别）
    /// </summary>
    public class ContinuousGestureConfig
    {
        /// <summary>
        /// 是否启用此配置
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 手指数量 (2, 3, 4)
        /// </summary>
        public int ContactCount { get; set; } = 2;

        /// <summary>
        /// 是否启用缩放（仅 2 指有效）
        /// </summary>
        public bool EnableZoom { get; set; }

        /// <summary>
        /// 缩放速度
        /// </summary>
        public double ZoomSpeed { get; set; } = 1.0;

        /// <summary>
        /// 缩放检测灵敏度（0.1-3.0，默认 1.0）
        /// 值越小越容易进入缩放，越大需要更明显的捏合动作
        /// 实际阈值 = 基础阈值 * ZoomSensitivity
        /// </summary>
        public double ZoomSensitivity { get; set; } = 1.0;

        /// <summary>
        /// 滑动模式
        /// </summary>
        public ContinuousScrollMode ScrollMode { get; set; } = ContinuousScrollMode.InertialScroll;

        /// <summary>
        /// InertialScroll 模式的参数
        /// </summary>
        public InertialScrollSettings ScrollSettings { get; set; }

        /// <summary>
        /// 手势修饰符（Default/PrimaryButtonDown/Ctrl/Shift/Alt 的组合）
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public GestureModifiers Modifiers { get; set; } = GestureModifiers.Default;

        /// <summary>
        /// 自定义模式下，各方向的命令配置
        /// Key = Gestures 枚举值 (Up/Down/Left/Right)
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Dictionary<Gestures, List<Command>> DirectionCommands { get; set; }
    }

    /// <summary>
    /// 连续手势滑动模式
    /// </summary>
    public enum ContinuousScrollMode
    {
        /// <summary>
        /// 内置惯性滚动
        /// </summary>
        InertialScroll = 0,

        /// <summary>
        /// 自定义方向命令
        /// </summary>
        Custom = 1,

        /// <summary>
        /// 不启用连续滑动
        /// </summary>
        None = 2
    }

    /// <summary>
    /// 兼容旧格式：旧 JSON 中 ContinuousGestures 是数组，新格式是对象
    /// </summary>
    class ContinuousGestureSettingsConverter : JsonConverter<ContinuousGestureSettings>
    {
        public override ContinuousGestureSettings ReadJson(JsonReader reader, Type objectType,
            ContinuousGestureSettings existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var settings = existingValue ?? new ContinuousGestureSettings();

            if (reader.TokenType == JsonToken.StartArray)
            {
                // 旧格式：直接是 ContinuousGestureConfig 数组，读入 Configs
                var configs = serializer.Deserialize<List<ContinuousGestureConfig>>(reader);
                if (configs != null)
                    settings.Configs = configs;
            }
            else if (reader.TokenType == JsonToken.StartObject)
            {
                // 新格式：包含 InheritBits + Configs 的对象
                var obj = JObject.Load(reader);
                if (obj["InheritBits"] != null)
                    settings.InheritBits = obj["InheritBits"].Value<int>();
                if (obj["Configs"] != null)
                    settings.Configs = obj["Configs"].ToObject<List<ContinuousGestureConfig>>(serializer) ?? new List<ContinuousGestureConfig>();
            }

            return settings;
        }

        public override void WriteJson(JsonWriter writer, ContinuousGestureSettings value, JsonSerializer serializer)
        {
            // 始终写新格式
            writer.WriteStartObject();
            if (value.InheritBits != 0b111)
            {
                writer.WritePropertyName("InheritBits");
                writer.WriteValue(value.InheritBits);
            }
            if (value.Configs.Count > 0)
            {
                writer.WritePropertyName("Configs");
                serializer.Serialize(writer, value.Configs);
            }
            writer.WriteEndObject();
        }
    }
}
