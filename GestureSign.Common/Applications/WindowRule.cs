using ManagedWinapi.Windows;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace GestureSign.Common.Applications
{
    /// <summary>
    /// 窗口匹配规则（直接定义匹配条件）
    /// 用于：预置窗口规则、应用匹配规则、优先级窗口
    /// </summary>
    public class WindowRule : IWindowRule
    {
        private List<MatchCondition> _effectiveConditions;

        /// <summary>
        /// 规则唯一标识（保存时自动生成）
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// 确保 ID 已初始化（保存前调用）
        /// </summary>
        public void EnsureId()
        {
            if (string.IsNullOrEmpty(Id))
            {
                Id = Guid.NewGuid().ToString();
            }
        }

        /// <summary>
        /// 规则名称（预置规则的显示名称，普通规则可为空）
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 窗口激活方式（仅预置规则使用）。
        /// UseGlobal(0)：使用全局设置（默认）。
        /// </summary>
        public ActivationMethod ActivationMethod { get; set; } = ActivationMethod.UseGlobal;

        /// <summary>
        /// 应用程序路径（降级匹配：当 Conditions 为空时使用）
        /// </summary>
        private string _applicationPath;
        public string ApplicationPath
        {
            get => _applicationPath;
            set
            {
                _applicationPath = value;
                InvalidateEffectiveConditions();
            }
        }

        /// <summary>
        /// 匹配条件列表（AND 关系）
        /// </summary>
        private List<MatchCondition> _conditions = new List<MatchCondition>();
        public List<MatchCondition> Conditions
        {
            get => _conditions;
            set
            {
                _conditions = value ?? new List<MatchCondition>();
                InvalidateEffectiveConditions();
            }
        }

        /// <summary>
        /// 使缓存失效，下次访问时重新计算
        /// </summary>
        private void InvalidateEffectiveConditions()
        {
            _effectiveConditions = null;
        }

        /// <summary>
        /// 获取用于匹配的条件（初始化时计算，缓存结果）
        /// </summary>
        [JsonIgnore]
        public List<MatchCondition> EffectiveConditions
        {
            get
            {
                if (_effectiveConditions == null)
                {
                    _effectiveConditions = ComputeEffectiveConditions();
                }
                return _effectiveConditions;
            }
        }

        /// <summary>
        /// 计算有效匹配条件
        /// 优先级：Conditions > ApplicationPath 生成的条件
        /// </summary>
        private List<MatchCondition> ComputeEffectiveConditions()
        {
            // 1. 优先使用 Conditions
            if (_conditions?.Count > 0)
                return _conditions;

            // 2. 降级：从 ApplicationPath 生成条件
            if (!string.IsNullOrEmpty(_applicationPath))
            {
                return new List<MatchCondition>
                {
                    new MatchCondition
                    {
                        Type = MatchConditionType.ProcessPath,
                        Value = _applicationPath
                    }
                };
            }

            // 3. 无条件，返回空列表
            return new List<MatchCondition>();
        }

        /// <summary>
        /// 判断窗口是否匹配
        /// </summary>
        public bool IsMatch(SystemWindow window)
        {
            return IsMatch(new WindowInfoCache(window));
        }

        /// <summary>
        /// 判断窗口是否匹配（使用缓存）
        /// </summary>
        public bool IsMatch(WindowInfoCache windowInfo)
        {
            var conditions = EffectiveConditions;
            // 无条件时跳过匹配，返回 false
            if (conditions == null || conditions.Count == 0)
                return false;

            return WindowMatcher.MatchAllConditions(windowInfo, conditions);
        }

        /// <summary>
        /// 获取显示名称（用于列表展示）
        /// </summary>
        public string GetDisplayName()
        {
            if (!string.IsNullOrEmpty(Name))
                return Name;

            if (_conditions?.Count > 0)
            {
                var first = _conditions[0];
                var suffix = _conditions.Count > 1 ? $" (+{_conditions.Count - 1})" : "";
                return $"{first.Value}{suffix}";
            }

            if (!string.IsNullOrEmpty(_applicationPath))
                return Path.GetFileName(_applicationPath);

            return "(空规则)";
        }

        /// <summary>
        /// 显示名称属性（用于 WPF 绑定）
        /// </summary>
        [JsonIgnore]
        public string DisplayName => GetDisplayName();
    }
}
