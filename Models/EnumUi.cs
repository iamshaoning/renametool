namespace RenameTool.Models;

/// <summary>下拉选项：Value 用于绑定，Label 用于展示。</summary>
public sealed class Option
{
	public required object Value { get; init; }
	public required string Label { get; init; }

	/// <summary>下拉框未套用模板时按此文本呈现，避免显示类型全名。</summary>
	public override string ToString() => Label;
}

/// <summary>UI 下拉可选项集。</summary>
public static class EnumUi
{
	public static IReadOnlyList<Option> CaseModes { get; } =
	[
		new() { Value = CaseMode.None, Label = "不转换" },
		new() { Value = CaseMode.UpperCase, Label = "全部大写" },
		new() { Value = CaseMode.LowerCase, Label = "全部小写" },
		new() { Value = CaseMode.TitleCase, Label = "每词首字母大写" },
		new() { Value = CaseMode.SentenceCase, Label = "整句首字母大写" },
	];

	public static IReadOnlyList<Option> WordStyles { get; } =
	[
		new() { Value = WordStyle.None, Label = "不处理分隔符" },
		new() { Value = WordStyle.SpaceToDash, Label = "空格 → 连字符" },
		new() { Value = WordStyle.SpaceToUnderscore, Label = "空格 → 下划线" },
		new() { Value = WordStyle.DashToSpace, Label = "连字符 → 空格" },
		new() { Value = WordStyle.DashToUnderscore, Label = "连字符 → 下划线" },
		new() { Value = WordStyle.UnderscoreToSpace, Label = "下划线 → 空格" },
		new() { Value = WordStyle.UnderscoreToDash, Label = "下划线 → 连字符" },
	];

	public static IReadOnlyList<Option> InsertPositions { get; } =
	[
		new() { Value = InsertPosition.Start, Label = "开头" },
		new() { Value = InsertPosition.End, Label = "末尾" },
		new() { Value = InsertPosition.Index, Label = "指定位置" },
	];

	public static IReadOnlyList<Option> SeqTypes { get; } =
	[
		new() { Value = SeqType.Numeric, Label = "数字（01、02…）" },
		new() { Value = SeqType.Alpha, Label = "字母（A、B…AA）" },
		new() { Value = SeqType.Roman, Label = "罗马数字（I、II…）" },
	];

	public static IReadOnlyList<Option> SeqScopes { get; } =
	[
		new() { Value = SeqScope.Global, Label = "全部文件连续编号" },
		new() { Value = SeqScope.PerFolder, Label = "每个文件夹重新编号" },
		new() { Value = SeqScope.PerExtension, Label = "按扩展名分组" },
	];

	public static IReadOnlyList<Option> SeqPositions { get; } =
	[
		new() { Value = SeqPosition.Start, Label = "添加在名称前" },
		new() { Value = SeqPosition.End, Label = "添加在名称后" },
		new() { Value = SeqPosition.Replace, Label = "替换整个名称" },
	];

	public static IReadOnlyList<Option> SeqSorts { get; } =
	[
		new() { Value = SeqSortBy.ListOrder, Label = "当前列表顺序" },
		new() { Value = SeqSortBy.Name, Label = "按名称" },
		new() { Value = SeqSortBy.Size, Label = "按大小" },
		new() { Value = SeqSortBy.Modified, Label = "按修改时间" },
		new() { Value = SeqSortBy.Extension, Label = "按扩展名" },
	];

	public static IReadOnlyList<Option> CleanupModes { get; } =
	[
		new() { Value = CleanupMode.Cleanup, Label = "按字符类别清除" },
		new() { Value = CleanupMode.Chars, Label = "从端部删除 N 个字符" },
		new() { Value = CleanupMode.Range, Label = "删除指定区间" },
	];

	public static IReadOnlyList<Option> Directions { get; } =
	[
		new() { Value = CleanupDirection.Start, Label = "开头" },
		new() { Value = CleanupDirection.End, Label = "末尾" },
	];

	public static IReadOnlyList<Option> RuleTypes { get; } =
	[
		new() { Value = RuleType.FindReplace, Label = RuleTypeNames.Label(RuleType.FindReplace) },
		new() { Value = RuleType.Insert, Label = RuleTypeNames.Label(RuleType.Insert) },
		new() { Value = RuleType.Sequence, Label = RuleTypeNames.Label(RuleType.Sequence) },
		new() { Value = RuleType.NameTemplate, Label = RuleTypeNames.Label(RuleType.NameTemplate) },
		new() { Value = RuleType.CaseStyle, Label = RuleTypeNames.Label(RuleType.CaseStyle) },
		new() { Value = RuleType.RemoveCleanup, Label = RuleTypeNames.Label(RuleType.RemoveCleanup) },
	];

	public static IReadOnlyList<Option> Scopes { get; } =
	[
		new() { Value = ExtensionScope.Name, Label = "文件名" },
		new() { Value = ExtensionScope.Extension, Label = "扩展名" },
		new() { Value = ExtensionScope.Full, Label = "完整名称" },
	];
}
