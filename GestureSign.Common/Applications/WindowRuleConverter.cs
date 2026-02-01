using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace GestureSign.Common.Applications
{
    /// <summary>
    /// IWindowRule 的 JSON 转换器
    /// 支持向后兼容：旧格式 List&lt;MatchCondition&gt; 会被转换为 WindowRule
    /// </summary>
    public class WindowRuleConverter : JsonConverter<IWindowRule>
    {
        // 用于反序列化具体类型的 serializer（不包含此 converter，避免无限递归）
        private static readonly JsonSerializer _innerSerializer = new JsonSerializer
        {
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Include
        };

        public override IWindowRule ReadJson(JsonReader reader, Type objectType, IWindowRule existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;

            var token = JToken.Load(reader);

            // 旧格式：数组 (List<MatchCondition>)
            if (token.Type == JTokenType.Array)
            {
                var conditions = token.ToObject<List<MatchCondition>>(_innerSerializer);
                return new WindowRule { Conditions = conditions ?? new List<MatchCondition>() };
            }

            // 新格式：对象
            if (token.Type == JTokenType.Object)
            {
                var obj = (JObject)token;

                // 检查是否有 $type 字段
                var typeName = obj["$type"]?.Value<string>();

                if (typeName != null)
                {
                    // 根据类型名称判断
                    if (typeName.Contains("WindowRuleRef"))
                    {
                        return obj.ToObject<WindowRuleRef>(_innerSerializer);
                    }
                    else if (typeName.Contains("WindowRule"))
                    {
                        return obj.ToObject<WindowRule>(_innerSerializer);
                    }
                }

                // 没有 $type，根据属性判断
                if (obj.ContainsKey("PresetName"))
                {
                    return obj.ToObject<WindowRuleRef>(_innerSerializer);
                }
                else
                {
                    return obj.ToObject<WindowRule>(_innerSerializer);
                }
            }

            throw new JsonSerializationException($"Unexpected token type {token.Type} when deserializing IWindowRule");
        }

        public override void WriteJson(JsonWriter writer, IWindowRule value, JsonSerializer serializer)
        {
            // 写入时使用默认的序列化逻辑（包含 $type）
            var token = JToken.FromObject(value, new JsonSerializer
            {
                TypeNameHandling = TypeNameHandling.Objects,
                NullValueHandling = NullValueHandling.Ignore
            });
            token.WriteTo(writer);
        }
    }
}
