using System;

namespace GestureSign.Common.Applications
{
	/// <summary>
	/// Legacy enum for backward compatibility with old .act files
	/// </summary>
	[Obsolete("Use MatchCondition/MatchConditionType instead")]
	public enum MatchUsing
	{
		WindowClass = 0,
		WindowTitle = 1,
		ExecutableFilename = 2,
		All = 4,

		// 新增匹配类型
		AUMID = 5,              // 应用用户模型 ID（UWP/PWA）
		ClassNameAndPath = 6    // 窗口类名 + 完整进程路径组合
	}
}