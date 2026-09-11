using RenameTool.Engine;

namespace RenameTool.Models;

public enum PreviewIssue
{
	None,
	Conflict,   // 同目录同名
	EmptyName,  // 结果为空
	Illegal,    // 含非法字符 / 保留名
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

	public bool HasChange => !NameUtils.ExactName(OriginalName, NewName);
	public bool IsOk => Issue == PreviewIssue.None;
	public bool CanRename => IsInScope && HasChange && IsOk;

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

	/// <summary>最后一列：成功显示新名称，失败显示失败原因。</summary>
	public string Result => Status == "failed" ? (Reason ?? NewName) : NewName;

	private bool _isNameExpanded;
	/// <summary>名称较长时，点击名称切换为自动换行完整显示。</summary>
	public bool IsNameExpanded
	{
		get => _isNameExpanded;
		set => SetProperty(ref _isNameExpanded, value);
	}
}
