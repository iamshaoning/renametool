using RenameTool.Engine;

namespace RenameTool.Models;

public enum PreviewIssue
{
	None,
	Conflict,        // 同目录同名 / 目标文件已存在
	EmptyName,       // 结果为空
	Illegal,         // 含非法字符 / 保留名
	TooLong,         // 目标完整路径过长
	SequenceOverflow,// 序号规则的起始值 + 步长累加越界，无法生成合法序号
}

/// <summary>执行前体检发现命名冲突时的处理策略。</summary>
public enum ConflictPolicy
{
	Skip,        // 跳过冲突项，执行其余
	AutoNumber,  // 为冲突项自动追加序号 (1)(2)…
	Abort,       // 存在冲突则整体中止
}

/// <summary>一条预览结果（每次重算后整体重建，无需逐字段通知）。</summary>
public sealed class PreviewItem : ObservableObject, INameExpandable
{
	public required FileItem File { get; init; }
	public required string OriginalName { get; init; }
	public required string NewName { get; init; }
	public required PreviewIssue Issue { get; init; }

	/// <summary>是否在当前筛选范围内（参与改名）。范围外行仅作展示。</summary>
	public required bool IsInScope { get; init; }

	/// <summary>该行目标名是否被用户手动锁定（锁定期间不随规则变化）。</summary>
	public required bool IsLocked { get; init; }

	public bool HasChange => !NameUtils.ExactName(OriginalName, NewName);

	/// <summary>目录简短显示：与上一文件同目录则空白，否则显示目录名。</summary>
	public required string DirDisplay { get; init; }

	private bool _isNameExpanded;
	/// <summary>名称较长时，点击名称切换为自动换行完整显示。</summary>
	public bool IsNameExpanded
	{
		get => _isNameExpanded;
		set => SetProperty(ref _isNameExpanded, value);
	}

	public string IssueText => Issue switch
	{
		PreviewIssue.Conflict => "冲突",
		PreviewIssue.EmptyName => "空名",
		PreviewIssue.Illegal => "非法字符",
		PreviewIssue.TooLong => "路径过长",
		PreviewIssue.SequenceOverflow => "序号越界",
		_ => "",
	};
}

/// <summary>执行/撤销日志条目。</summary>
public sealed class LogItem : ObservableObject, INameExpandable
{
	public required string Original { get; init; }
	public required string NewName { get; init; }
	public required string Status { get; init; } // success / failed / skipped
	public string? Reason { get; init; }

	/// <summary>该日志对应的列表项（体检跳过的条目可能没有）；用于“仅重试失败项”。</summary>
	public FileItem? File { get; init; }

	/// <summary>最后一列：有原因（失败/跳过）时显示原因，否则显示新名称。</summary>
	public string Result => string.IsNullOrEmpty(Reason) ? NewName : Reason;

	private bool _isNameExpanded;
	/// <summary>名称较长时，点击名称切换为自动换行完整显示。</summary>
	public bool IsNameExpanded
	{
		get => _isNameExpanded;
		set => SetProperty(ref _isNameExpanded, value);
	}
}
