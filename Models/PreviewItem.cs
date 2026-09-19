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
