namespace RenameTool.Models;

/// <summary>六种重命名规则。</summary>
public enum RuleType
{
	FindReplace,
	Insert,
	Sequence,
	NameTemplate,
	CaseStyle,
	RemoveCleanup,
}

/// <summary>规则作用于名称的哪个部分。</summary>
public enum ExtensionScope
{
	Name,      // 仅文件名（不含扩展名）
	Extension, // 仅扩展名
	Full,      // 完整文件名（含扩展名）
}

/// <summary>大小写 / 单词样式。</summary>
public enum CaseMode
{
	None,
	UpperCase,
	LowerCase,
	TitleCase,
	SentenceCase,
}

/// <summary>空格 / 连字符 / 下划线互转样式。</summary>
public enum WordStyle
{
	None,
	SpaceToDash,
	SpaceToUnderscore,
	DashToSpace,
	UnderscoreToSpace,
	DashToUnderscore,
	UnderscoreToDash,
}

/// <summary>序号进制格式。</summary>
public enum SeqType
{
	Numeric,
	Alpha,
	Roman,
}

/// <summary>序号作用范围。</summary>
public enum SeqScope
{
	Global,       // 全局连续
	PerFolder,    // 每个目录重新开始
	PerExtension, // 按扩展名分组
}

/// <summary>序号插入位置。</summary>
public enum SeqPosition
{
	Start,   // 前缀
	End,     // 后缀
	Replace, // 整个（文件名/扩展名）替换为序号
}

/// <summary>文本插入位置。</summary>
public enum InsertPosition
{
	Start,
	End,
	Index,
}

/// <summary>删除/清洗模式。</summary>
public enum CleanupMode
{
	Chars,   // 从开头/结尾删除 N 个字符
	Range,   // 删除指定区间
	Cleanup, // 按字符类别清除
}

/// <summary>字符位置方向。</summary>
public enum CleanupDirection
{
	Start,
	End,
}

/// <summary>文件列表排序方式。</summary>
public enum SortMode
{
	Import,     // 导入顺序
	NameAsc,    // 名称 A-Z
	NameDesc,   // 名称 Z-A
	ExtAsc,     // 扩展名 A-Z
	ExtDesc,    // 扩展名 Z-A
}

/// <summary>编号前排序字段。</summary>
public enum SeqSortBy
{
	ListOrder,
	Name,
	Size,
	Modified,
	Extension,
}
