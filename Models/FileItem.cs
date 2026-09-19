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
		// 构造期间不做任何属性变更通知（此刻还没有任何订阅者），因此可直接读盘并赋值；
		// 需要后台批量构造的调用方则改用 ReadDisk + ApplyDiskState 两段式。
		var state = ReadDisk(fullPath);
		if (state.Exists)
		{
			_size = state.Size;
			_modified = state.Modified;
			_created = state.Created;
		}
	}

	/// <summary>一个文件在磁盘上的状态快照。</summary>
	public readonly record struct DiskState(bool Exists, long Size, DateTime Modified, DateTime Created)
	{
		/// <summary>文件不存在（或不可访问）时的状态。</summary>
		public static readonly DiskState Missing = new(false, 0, default, default);
	}

	/// <summary>
	/// 读取文件的磁盘状态。这是本类唯一会访问磁盘的成员，调用方可在后台线程执行，
	/// 再用 <see cref="ApplyDiskState"/> 回到 UI 线程赋值——批量导入 / 刷新时把上千次磁盘查询
	/// 留在界面线程会让窗口完全冻住。
	/// 一次 <see cref="FileInfo"/> 取齐大小 / 修改时间 / 创建时间：原先分别调用 File.Exists、
	/// FileInfo.Length、File.GetLastWriteTime 共 3 次磁盘查询，大目录下开销明显；
	/// 创建时间此前从未赋值，导致 {created} 变量恒为 0001-01-01，这里一并补上。
	/// </summary>
	public static DiskState ReadDisk(string fullPath)
	{
		try
		{
			var info = new FileInfo(fullPath);
			if (!info.Exists) return DiskState.Missing;
			return new DiskState(true, info.Length, info.LastWriteTime, info.CreationTime);
		}
		catch
		{
			// 路径过长、非法字符、无访问权限等：按“不存在”处理，与原先 File.Exists 返回 false 的语义一致
			return DiskState.Missing;
		}
	}

	/// <summary>
	/// 把 <see cref="ReadDisk"/> 的结果写回条目属性（会触发绑定通知，须在 UI 线程调用）。
	/// 文件已不存在则标记失效且不可选中。
	/// </summary>
	public void ApplyDiskState(DiskState state)
	{
		if (!state.Exists)
		{
			IsMissing = true;
			Selected = false;
			return;
		}
		IsMissing = false;
		Size = state.Size;
		Modified = state.Modified;
		Created = state.Created;
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
		private set
		{
			// SizeText 由 Size 派生：不通知的话，刷新 / 改名后列表里的大小文字会停在旧值
			if (SetProperty(ref _size, value)) RaisePropertyChanged(nameof(SizeText));
		}
	}

	private DateTime _modified;
	public DateTime Modified
	{
		get => _modified;
		private set
		{
			// ModifiedText 由 Modified 派生，同上
			if (SetProperty(ref _modified, value)) RaisePropertyChanged(nameof(ModifiedText));
		}
	}

	private DateTime _created;
	/// <summary>文件创建时间（供 {created} 变量）。</summary>
	public DateTime Created
	{
		get => _created;
		private set => SetProperty(ref _created, value);
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

	private string? _lockedName;
	/// <summary>用户手动锁定的目标文件名：非空时预览与执行一律采用该名称，不随规则变化，直到手动解除。</summary>
	public string? LockedName
	{
		get => _lockedName;
		set => SetProperty(ref _lockedName, value);
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

	private int _depth;
	/// <summary>列表中的缩进深度（所属文件夹深度 + 1），使文件与所在文件夹保持一致的层级缩进。</summary>
	public int Depth
	{
		get => _depth;
		set => SetProperty(ref _depth, value);
	}

	public string SizeText => Size.FormatFileSize();
	public string ModifiedText => Modified.ToString("yyyy-MM-dd HH:mm");

	/// <summary>磁盘改名成功后同步条目显示（目标名锁定随之解除，因为该名称已经落地）。</summary>
	public void ApplyDiskRename(string newName)
	{
		string newPath = System.IO.Path.Combine(Directory, newName);
		(var newBase, var newExt) = NameUtils.Split(newName);
		BaseName = newBase;
		Extension = newExt;
		Name = newName;
		FullPath = newPath;
		LockedName = null;
		// 改名刚在磁盘上成功落地，文件必然存在：顺手清掉可能残留的失效标记。
		// 否则一旦「按旧路径读盘」的结果（不存在）晚于本方法落到条目上，就会出现
		// 一条「名字已是新名、却又标着失效」的条目，与磁盘上的真实文件看起来重复。
		IsMissing = false;
	}

	/// <summary>按磁盘最新状态刷新大小与时间；文件已不存在则标记失效（失效条目不可选中）。</summary>
	public void RefreshFromDisk() => ApplyDiskState(ReadDisk(FullPath));
}
