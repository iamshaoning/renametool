using RenameTool.Models;

namespace RenameTool.Engine;

/// <summary>文件列表筛选：按文件名包含匹配（忽略大小写）。</summary>
public sealed class FilterSpec
{
	public string Value { get; set; } = "";

	public bool IsActive => !string.IsNullOrEmpty(Value);

	public bool Matches(FileItem file) =>
		!IsActive || file.Name.Contains(Value, StringComparison.OrdinalIgnoreCase);
}
