namespace RenameTool.Models;

public static class RuleTypeNames
{
	public static string Label(RuleType type) => type switch
	{
		RuleType.FindReplace => "查找替换",
		RuleType.Insert => "插入文本",
		RuleType.Sequence => "添加序号",
		RuleType.NameTemplate => "名称模板",
		RuleType.CaseStyle => "大小写处理",
		RuleType.RemoveCleanup => "删除清洗",
		RuleType.PairSwap => "成对交换",
		_ => type.ToString(),
	};
}
