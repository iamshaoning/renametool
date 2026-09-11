using System.IO;
using RenameTool.Engine;

namespace RenameTool.Models;

/// <summary>名称支持点击展开换行的列表项（文件列表 / 预览 / 日志共用）。</summary>
public interface INameExpandable
{
	bool IsNameExpanded { get; set; }
}

/// <summary>单个待重命名文件。</summary>
public sealed class FileItem : ObservableObject, INameExpandable
{
	private string _fullPath;
	private string _name;
	private string _baseName;
	private string _extension;

	public FileItem(string fullPath)
	{
		_fullPath = fullPath;
		_directory = Path.GetDirectoryName(fullPath) ?? "";
		_name = Path.GetFileName(fullPath);
		(_baseName, _extension) = NameUtils.Split(_name);
		_size = File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0;
		_modified = File.Exists(fullPath) ? File.GetLastWriteTime(fullPath) : DateTime.MinValue;
	}

	private string _directory;
	public string Directory
	{
		get => _directory;
		private set => SetProperty(ref _directory, value);
	}

	public string FullPath
	{
		get => _fullPath;
		private set => SetProperty(ref _fullPath, value);
	}

	public string Name
	{
		get => _name;
		private set => SetProperty(ref _name, value);
	}

	public string BaseName
	{
		get => _baseName;
		private set => SetProperty(ref _baseName, value);
	}

	public string Extension
	{
		get => _extension;
		private set => SetProperty(ref _extension, value);
	}

	private long _size;
	public long Size
	{
		get => _size;
		private set => SetProperty(ref _size, value);
	}

	private DateTime _modified;
	public DateTime Modified
	{
		get => _modified;
		private set => SetProperty(ref _modified, value);
	}

	private bool _selected = true;
	public bool Selected
	{
		get => _selected;
		set => SetProperty(ref _selected, value);
	}

	private bool _isVisible = true;
	public bool IsVisible
	{
		get => _isVisible;
		set => SetProperty(ref _isVisible, value);
	}

	private bool _isMissing;
	/// <summary>执行前校验发现文件已不存在（被删除或改名），列表中醒目标记。</summary>
	public bool IsMissing
	{
		get => _isMissing;
		set => SetProperty(ref _isMissing, value);
	}

	private bool _isNameExpanded;
	/// <summary>名称较长时，点击名称切换为自动换行完整显示。</summary>
	public bool IsNameExpanded
	{
		get => _isNameExpanded;
		set => SetProperty(ref _isNameExpanded, value);
	}

	private bool _isFolderSource;
	/// <summary>来源标记：true = 通过“导入文件夹”加入（含后导入文件夹时合并而来），false = 单独导入的文件。</summary>
	public bool IsFolderSource
	{
		get => _isFolderSource;
		set => SetProperty(ref _isFolderSource, value);
	}

	private string? _sourceRoot;
	/// <summary>文件夹导入的根目录（用于刷新）；单独导入的文件为空。</summary>
	public string? SourceRoot
	{
		get => _sourceRoot;
		set => SetProperty(ref _sourceRoot, value);
	}

	public string SizeText => Size.FormatFileSize();
	public string ModifiedText => Modified.ToString("yyyy-MM-dd HH:mm");

	/// <summary>磁盘改名成功后同步条目显示。</summary>
	public void ApplyDiskRename(string newName)
	{
		string newPath = System.IO.Path.Combine(Directory, newName);
		(var newBase, var newExt) = NameUtils.Split(newName);
		BaseName = newBase;
		Extension = newExt;
		Name = newName;
		FullPath = newPath;
	}

	/// <summary>按磁盘最新状态刷新大小与时间；文件已不存在则标记失效（失效条目不可选中）。</summary>
	public void RefreshFromDisk()
	{
		if (!File.Exists(FullPath))
		{
			IsMissing = true;
			Selected = false;
			return;
		}
		IsMissing = false;
		var info = new FileInfo(FullPath);
		Size = info.Length;
		Modified = info.LastWriteTime;
	}
}
