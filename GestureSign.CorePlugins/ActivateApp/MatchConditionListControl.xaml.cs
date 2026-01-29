using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using GestureSign.Common.Applications;
using GestureSign.Common.UI;

namespace GestureSign.CorePlugins.ActivateApp
{
    /// <summary>
    /// MatchConditionListControl.xaml 的交互逻辑
    /// </summary>
    public partial class MatchConditionListControl : UserControl
    {
        public MatchConditionListControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 获取所有条件（仅返回有值的条件）
        /// </summary>
        public List<MatchCondition> GetConditions()
        {
            var conditions = new List<MatchCondition>();
            foreach (var child in ConditionPanel.Children)
            {
                if (child is MatchConditionItemControl item)
                {
                    var condition = item.GetCondition();
                    if (condition != null)
                    {
                        conditions.Add(condition);
                    }
                }
            }
            return conditions;
        }

        /// <summary>
        /// 设置条件列表
        /// </summary>
        public void SetConditions(List<MatchCondition> conditions)
        {
            ConditionPanel.Children.Clear();
            if (conditions == null || conditions.Count == 0)
                return;

            foreach (var condition in conditions)
            {
                AddConditionItem(condition);
            }
        }

        /// <summary>
        /// 从窗口信息填充条件（捕获模式，默认勾选 ProcessPath）
        /// </summary>
        public void PopulateFromWindowInfo(WindowMatchInfo info)
        {
            ConditionPanel.Children.Clear();

            // 默认添加 ProcessPath（如果有）
            if (!string.IsNullOrEmpty(info.ProcessPath))
            {
                AddConditionItem(new MatchCondition
                {
                    Type = MatchConditionType.ProcessPath,
                    Value = info.ProcessPath
                });
            }

            // 添加 ClassName（如果有）
            if (!string.IsNullOrEmpty(info.ClassName))
            {
                AddConditionItem(new MatchCondition
                {
                    Type = MatchConditionType.ClassName,
                    Value = info.ClassName
                });
            }

            // 添加 AUMID（如果有，UWP 应用）
            if (!string.IsNullOrEmpty(info.AUMID))
            {
                AddConditionItem(new MatchCondition
                {
                    Type = MatchConditionType.AUMID,
                    Value = info.AUMID
                });
            }
        }

        /// <summary>
        /// 清空所有条件
        /// </summary>
        public void Clear()
        {
            ConditionPanel.Children.Clear();
        }

        /// <summary>
        /// 添加条件项
        /// </summary>
        private MatchConditionItemControl AddConditionItem(MatchCondition condition = null)
        {
            var item = new MatchConditionItemControl
            {
                Margin = new Thickness(0, 0, 0, 5)
            };

            if (condition != null)
            {
                item.SetCondition(condition);
            }

            item.DeleteRequested += (s, e) =>
            {
                ConditionPanel.Children.Remove(item);
            };

            ConditionPanel.Children.Add(item);
            return item;
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            AddConditionItem();
        }
    }
}
