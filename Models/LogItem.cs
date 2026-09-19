namespace RenameTool.Models;

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
