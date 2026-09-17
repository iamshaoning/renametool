using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;

namespace RenameTool.Models;

/// <summary>
/// 文件列表中的文件夹节点：维护文件夹之间的父子层级关系与展开 / 折叠状态。
/// 文件名与所在文件夹一一对应，层级仅按“同列表内出现的祖先目录”建立。
/// </summary>
public sealed class FolderNode : ObservableObject
{
	private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

	public FolderNode(string fullPath)
	{
		FullPath = fullPath;
	}

	/// <summary>文件夹完整路径，同时作为节点唯一标识。</summary>
	public string FullPath { get; }

	/// <summary>父节点；顶层文件夹为空。</summary>
	public FolderNode? Parent { get; internal set; }

	/// <summary>层级深度：顶层为 0，逐级 +1。</summary>
	public int Depth { get; internal set; }

	/// <summary>子文件夹（按首次出现顺序）。</summary>
	public List<FolderNode> Folders { get; } = [];

	/// <summary>直接位于本文件夹下的文件（按列表顺序）。</summary>
	public List<FileItem> Files { get; } = [];

	/// <summary>
	/// 本文件夹模块自带的展示容器所绑定的行：先本层文件，再子文件夹。
	/// 每个子文件夹也是独立模块、且带自己的容器，因此容器可以一层包一层，支持任意层级嵌套。
	/// </summary>
	public ObservableCollection<object> Rows { get; } = [];

	/// <summary>本文件夹（含所有子文件夹）的文件总数，折叠时用于提示隐藏数量。</summary>
	public int TotalCount { get; internal set; }

	/// <summary>表头显示名：顶层显示完整路径，子级仅显示本层文件夹名。</summary>
	public string Name
	{
		get
		{
			if (Depth == 0) return FullPath;
			string name = Path.GetFileName(FullPath.TrimEnd(Separators));
			return string.IsNullOrEmpty(name) ? FullPath : name;
		}
	}

	private bool _isExpanded = true;
	/// <summary>是否展开：折叠后其子文件夹与文件在列表中隐藏。</summary>
	public bool IsExpanded
	{
		get => _isExpanded;
		set => SetProperty(ref _isExpanded, value);
	}

	/// <summary>本文件夹（含所有子文件夹）下的全部文件。</summary>
	public IEnumerable<FileItem> AllFiles()
	{
		foreach (var file in Files) yield return file;
		foreach (var folder in Folders)
			foreach (var file in folder.AllFiles()) yield return file;
	}

	/// <summary>三态勾选状态：全部选中 true / 全未选中 false / 部分选中 null（失效条目不可选，不参与计算）。</summary>
	public bool? SelectionState
	{
		get
		{
			bool any = false, all = true, none = true;
			foreach (var file in AllFiles())
			{
				if (file.IsMissing) continue;
				any = true;
				if (file.Selected) none = false;
				else all = false;
			}
			if (!any || none) return false;
			return all ? true : null;
		}
	}

	/// <summary>来源标识：整组均为文件夹导入 / 均为文件导入 / 混合。</summary>
	public string SourceText
	{
		get
		{
			bool anyFolder = false, anyFile = false;
			foreach (var file in AllFiles())
			{
				if (file.IsFolderSource) anyFolder = true;
				else anyFile = true;
			}
			if (anyFolder && anyFile) return "混合来源";
			return anyFolder ? "文件夹导入" : "文件导入";
		}
	}

	/// <summary>订阅本层文件的勾选 / 失效 / 来源变化，用于刷新表头状态。</summary>
	internal void Attach()
	{
		foreach (var file in Files) file.PropertyChanged += OnFileChanged;
	}

	internal void Detach()
	{
		foreach (var file in Files) file.PropertyChanged -= OnFileChanged;
	}

	private void OnFileChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(FileItem.Selected) or nameof(FileItem.IsMissing))
			RaisePropertyChanged(nameof(SelectionState));
		if (e.PropertyName is nameof(FileItem.IsFolderSource))
			RaisePropertyChanged(nameof(SourceText));
	}
}
