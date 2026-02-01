using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GestureSign.Common.Configuration;
using GestureSign.Common.Log;

namespace GestureSign.Common.Applications
{
    /// <summary>
    /// 预置窗口规则管理器
    /// </summary>
    public class WindowPresetManager
    {
        #region Singleton

        private static readonly Lazy<WindowPresetManager> _instance =
            new Lazy<WindowPresetManager>(() => new WindowPresetManager());

        public static WindowPresetManager Instance => _instance.Value;

        private WindowPresetManager()
        {
            Presets = new List<WindowRule>();
        }

        #endregion

        #region Properties

        /// <summary>
        /// 预置规则列表
        /// </summary>
        public List<WindowRule> Presets { get; private set; }

        /// <summary>
        /// 预置规则文件路径
        /// </summary>
        public static string PresetsFilePath =>
            Path.Combine(AppConfig.ApplicationDataPath, Constants.WindowPresetsFileName);

        #endregion

        #region Events

        public event EventHandler PresetsChanged;

        protected virtual void OnPresetsChanged()
        {
            PresetsChanged?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 根据名称获取预置规则
        /// </summary>
        public WindowRule GetPresetByName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            return Presets.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 根据 ID 获取预置规则
        /// </summary>
        public WindowRule GetPresetById(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            return Presets.FirstOrDefault(p =>
                string.Equals(p.Id, id, StringComparison.Ordinal));
        }

        /// <summary>
        /// 添加预置规则
        /// </summary>
        public void AddPreset(WindowRule preset)
        {
            if (preset == null || string.IsNullOrEmpty(preset.Name))
                return;

            // 检查是否已存在同名规则
            var existing = GetPresetByName(preset.Name);
            if (existing != null)
            {
                // 更新现有规则
                existing.ApplicationPath = preset.ApplicationPath;
                existing.Conditions = preset.Conditions;
            }
            else
            {
                Presets.Add(preset);
            }

            OnPresetsChanged();
        }

        /// <summary>
        /// 移除预置规则
        /// </summary>
        public void RemovePreset(string name)
        {
            var preset = GetPresetByName(name);
            if (preset != null)
            {
                Presets.Remove(preset);
                OnPresetsChanged();
            }
        }

        /// <summary>
        /// 更新预置规则
        /// </summary>
        public void UpdatePreset(WindowRule preset)
        {
            if (preset == null || string.IsNullOrEmpty(preset.Name))
                return;

            var existing = GetPresetByName(preset.Name);
            if (existing != null)
            {
                existing.ApplicationPath = preset.ApplicationPath;
                existing.Conditions = preset.Conditions;
                OnPresetsChanged();
            }
        }

        /// <summary>
        /// 保存预置规则到文件
        /// </summary>
        public bool SavePresets()
        {
            try
            {
                // 确保所有预置规则都有 ID
                foreach (var preset in Presets)
                {
                    preset.EnsureId();
                }
                return FileManager.SaveObject(Presets, PresetsFilePath);
            }
            catch (Exception ex)
            {
                Logging.LogException(ex);
                return false;
            }
        }

        /// <summary>
        /// 从文件加载预置规则
        /// </summary>
        public Task LoadPresetsAsync()
        {
            return Task.Run(() => LoadPresets());
        }

        /// <summary>
        /// 从文件加载预置规则
        /// </summary>
        public void LoadPresets()
        {
            try
            {
                var loaded = FileManager.LoadObject<List<WindowRule>>(PresetsFilePath, false);
                if (loaded != null)
                {
                    Presets = loaded;
                }
                else
                {
                    Presets = new List<WindowRule>();
                }
            }
            catch (Exception ex)
            {
                Logging.LogException(ex);
                Presets = new List<WindowRule>();
            }
        }

        /// <summary>
        /// 查找引用了指定预置规则的所有位置
        /// </summary>
        public List<string> FindPresetReferences(string presetId)
        {
            var references = new List<string>();

            if (string.IsNullOrEmpty(presetId))
                return references;

            foreach (var app in ApplicationManager.Instance.Applications)
            {
                // 检查 MatchRules
                if (app.MatchRules != null)
                {
                    foreach (var rule in app.MatchRules)
                    {
                        if (rule is WindowRuleRef ruleRef &&
                            string.Equals(ruleRef.PresetId, presetId, StringComparison.Ordinal))
                        {
                            references.Add($"应用 \"{app.Name}\" 的匹配规则");
                            break;
                        }
                    }
                }

                // 检查 PriorityWindows
                if (app.PriorityWindows != null)
                {
                    foreach (var rule in app.PriorityWindows)
                    {
                        if (rule is WindowRuleRef ruleRef &&
                            string.Equals(ruleRef.PresetId, presetId, StringComparison.Ordinal))
                        {
                            references.Add($"应用 \"{app.Name}\" 的优先级窗口");
                            break;
                        }
                    }
                }
            }

            return references;
        }

        /// <summary>
        /// 清除对指定预置规则的所有引用
        /// </summary>
        public void ClearPresetReferences(string presetId)
        {
            if (string.IsNullOrEmpty(presetId))
                return;

            foreach (var app in ApplicationManager.Instance.Applications)
            {
                // 清除 MatchRules 中的引用
                if (app.MatchRules != null)
                {
                    app.MatchRules.RemoveAll(rule =>
                        rule is WindowRuleRef ruleRef &&
                        string.Equals(ruleRef.PresetId, presetId, StringComparison.Ordinal));
                }

                // 清除 PriorityWindows 中的引用
                if (app.PriorityWindows != null)
                {
                    app.PriorityWindows.RemoveAll(rule =>
                        rule is WindowRuleRef ruleRef &&
                        string.Equals(ruleRef.PresetId, presetId, StringComparison.Ordinal));
                }
            }
        }

        #endregion
    }
}
