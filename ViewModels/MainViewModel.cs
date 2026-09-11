using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using Microsoft.Win32;
using RenameTool.Engine;
using RenameTool.Models;

namespace RenameTool.ViewModels;

public enum PreviewFilter { All, Affected, Conflict }

/// <summary>主窗口 ViewModel：文件/规则/预览/执行。</summary>
public sealed class MainViewModel : ObservableObject
{
	private readonly RenameService _service = new();

	public ObservableCollection<FileItem> Files { get; } = [];
	/// <summary>文件列表 UI 绑定的可见子集（跟随搜索过滤）。</summary>
	public ObservableCollection<FileItem> FilesView { get; } = [];

	private ICollectionView? _filesGrouped;
	/// <summary>文件列表的显示视图：按所在文件夹分组（列表本身仍为扁平顺序）。</summary>
	public ICollectionView FilesGrouped
	{
		get
		{
			if (_filesGrouped is not null) return _filesGrouped;
			var view = CollectionViewSource.GetDefaultView(FilesView);
			view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FileItem.Directory)));
			_filesGrouped = view;
			return view;
		}
	}
	public ObservableCollection<RenameRule> Rules { get; } = [];
	public ObservableCollection<PreviewItem> Previews { get; } = [];
	public ObservableCollection<LogItem> Logs { get; } = [];

	private PreviewFilter _previewFilter = PreviewFilter.All;
	public PreviewFilter PreviewFilter
	{
		get => _previewFilter;
		set { if (SetProperty(ref _previewFilter, value)) RefreshPreviewList(); }
	}

	private string _filterText = "";
	public string FilterText
	{
		get => _filterText;
		set
		{
			if (SetProperty(ref _filterText, value)) { ApplyFilter(); }
		}
	}

	private bool _isExecuting;
	public bool IsExecuting
	{
		get => _isExecuting;
		set
		{
			if (SetProperty(ref _isExecuting, value))
			{
				// 这些判断只作为命令谓词使用（未直接绑定），须显式通知命令重新评估。
				ImportFilesCommand.RaiseCanExecuteChanged();
				ImportFolderCommand.RaiseCanExecuteChanged();
				RefreshCommand.RaiseCanExecuteChanged();
				ClearCommand.RaiseCanExecuteChanged();
				DeleteSelectedCommand.RaiseCanExecuteChanged();
				ExecuteCommand.RaiseCanExecuteChanged();
				// 执行状态变化后重新评估撤销/重做：
				// HistoryChanged 只在执行过程中触发（此时 IsExecuting 仍为 true），
				// 这里补一次评估，确保执行结束后按钮能正确启用。
				UndoCommand.RaiseCanExecuteChanged();
				RedoCommand.RaiseCanExecuteChanged();
			}
		}
	}

	private int _progressCurrent;
	public int ProgressCurrent
	{
		get => _progressCurrent;
		private set
		{
			if (SetProperty(ref _progressCurrent, value))
			{
				RaisePropertyChanged(nameof(ProgressPercent));
				RaisePropertyChanged(nameof(HasProgress));
			}
		}
	}

	private int _progressTotal;
	public int ProgressTotal
	{
		get => _progressTotal;
		private set
		{
			if (SetProperty(ref _progressTotal, value))
			{
				RaisePropertyChanged(nameof(HasProgress));
				RaisePropertyChanged(nameof(ProgressPercent));
			}
		}
	}

	public int ProgressPercent => ProgressTotal <= 0 ? 0 : (int)(ProgressCurrent * 100d / ProgressTotal);
	public bool HasProgress => ProgressTotal > 0;

	// 派生统计
	private int _affected;
	private int _conflicts;
	public int Conflicts => _conflicts;
	public int OkChangeCount => _affected - _conflicts;
	public int FileTotal => Files.Count;

	public string FileTotalText => $"共 {FileTotal} 个文件";
	public string SelectedText => $"{FilesView.Count(f => f.Selected)} / {FilesView.Count}";

	public bool CanExecute => !IsExecuting && Conflicts == 0 && OkChangeCount > 0;
	public bool CanImport => !IsExecuting;
	/// <summary>存在失效条目时才允许“清除失效”。</summary>
	public bool CanClearMissing => !IsExecuting && Files.Any(f => f.IsMissing);
	public bool HasFiles => Files.Count > 0;
	public bool HasRules => Rules.Count > 0;

	// 命令
	public RelayCommand ImportFilesCommand { get; }
	public RelayCommand ImportFolderCommand { get; }
	public RelayCommand RefreshCommand { get; }
	public RelayCommand ClearCommand { get; }
	public RelayCommand SelectAllCommand { get; }
	public RelayCommand SelectNoneCommand { get; }
	public RelayCommand DeleteSelectedCommand { get; }
	public RelayCommand ExecuteCommand { get; }
	public RelayCommand UndoCommand { get; }
	public RelayCommand RedoCommand { get; }
	public RelayCommand ClearLogCommand { get; }
	public RelayCommand<RuleType> AddRuleCommand { get; }
	public RelayCommand<RenameRule> RemoveRuleCommand { get; }
	public RelayCommand<RenameRule> DuplicateRuleCommand { get; }
	public RelayCommand<RenameRule> MoveRuleUpCommand { get; }
	public RelayCommand<RenameRule> MoveRuleDownCommand { get; }
	public RelayCommand<FileItem> RemoveFileCommand { get; }

	public IReadOnlyList<Option> SortModes { get; } =
	[
		new() { Value = SortMode.Import, Label = "导入顺序" },
		new() { Value = SortMode.NameAsc, Label = "名称 A-Z" },
		new() { Value = SortMode.NameDesc, Label = "名称 Z-A" },
		new() { Value = SortMode.ExtAsc, Label = "扩展名 A-Z" },
		new() { Value = SortMode.ExtDesc, Label = "扩展名 Z-A" },
	];

	public IReadOnlyList<Option> PreviewFilters { get; } =
	[
		new() { Value = PreviewFilter.All, Label = "全部" },
		new() { Value = PreviewFilter.Affected, Label = "有变化" },
		new() { Value = PreviewFilter.Conflict, Label = "冲突" },
	];

	private SortMode _sortMode;
	public SortMode SortMode
	{
		get => _sortMode;
		set
		{
			if (SetProperty(ref _sortMode, value)) { Resort(); }
		}
	}

	public MainViewModel()
	{
		ImportFilesCommand = new RelayCommand(ImportFiles, () => CanImport);
		ImportFolderCommand = new RelayCommand(ImportFolder, () => CanImport);
		RefreshCommand = new RelayCommand(() => RefreshFromSources(), () => CanImport && Files.Count > 0);
		ClearCommand = new RelayCommand(ClearMissingFiles, () => CanClearMissing);
		SelectAllCommand = new RelayCommand(() => SetAllSelected(true));
		SelectNoneCommand = new RelayCommand(() => SetAllSelected(false));
		DeleteSelectedCommand = new RelayCommand(DeleteSelected, () => !IsExecuting && Files.Any(f => f.Selected));
		ExecuteCommand = new RelayCommand(Execute, () => CanExecute);
		UndoCommand = new RelayCommand(Undo, () => _service.CanUndo && !IsExecuting);
		RedoCommand = new RelayCommand(Redo, () => _service.CanRedo && !IsExecuting);
		ClearLogCommand = new RelayCommand(() => Logs.Clear());
		AddRuleCommand = new RelayCommand<RuleType>(AddRule);
		RemoveRuleCommand = new RelayCommand<RenameRule>(r => { if (r is null) return; Rules.Remove(r); RecomputeAll(); });
		DuplicateRuleCommand = new RelayCommand<RenameRule>(DuplicateRule);
		MoveRuleUpCommand = new RelayCommand<RenameRule>(MoveUp);
		MoveRuleDownCommand = new RelayCommand<RenameRule>(MoveDown);
		RemoveFileCommand = new RelayCommand<FileItem>(RemoveFile);

		_service.HistoryChanged += () =>
		{
			UndoCommand.RaiseCanExecuteChanged();
			RedoCommand.RaiseCanExecuteChanged();
		};

		Files.CollectionChanged += OnFilesChanged;
		Rules.CollectionChanged += OnRulesChanged;
	}

	// ─────────────── 规则变更订阅 ───────────────

	private void OnRulesChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		// Move 仅改变位置，OldItems 与 NewItems 为同一项、成员关系未变，无需增删订阅。
		if (e.Action != NotifyCollectionChangedAction.Move)
		{
			if (e.NewItems is not null)
			{
				foreach (RenameRule item in e.NewItems)
				{
					item.PropertyChanged += OnRulePropertyChanged;
					item.Config.PropertyChanged += OnConfigPropertyChanged;
				}
			}
			if (e.OldItems is not null)
			{
				foreach (RenameRule item in e.OldItems)
				{
					item.PropertyChanged -= OnRulePropertyChanged;
					item.Config.PropertyChanged -= OnConfigPropertyChanged;
				}
			}
		}
		RaisePropertyChanged(nameof(HasRules));
		RecomputeAll();
	}

	private void OnRulePropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(RenameRule.Enabled) or nameof(RenameRule.Scope)) RecomputeAll();
	}

	/// <summary>规则参数变化后重算预览（各编辑器的绑定都会写回 Config 属性）。</summary>
	private void OnConfigPropertyChanged(object? sender, PropertyChangedEventArgs e) => RecomputeAll();

	// ─────────────── 导入 ───────────────

	private void ImportFiles()
	{
		var dlg = new OpenFileDialog
		{
			Multiselect = true,
			Title = "选择要重命名的文件",
		};
		if (dlg.ShowDialog() != true) return;
		AddPaths(dlg.FileNames);
	}

	private void ImportFolder()
	{
		var dlg = new OpenFolderDialog { Title = "选择文件夹（将递归导入其中全部文件）" };
		if (dlg.ShowDialog() != true) return;
		List<string> paths = [];
		CollectFiles(dlg.FolderName, paths);
		AddPaths(paths, fromFolder: true, root: dlg.FolderName);
	}

	private static void CollectFiles(string root, List<string> results)
	{
		try
		{
			foreach (string file in Directory.EnumerateFiles(root))
				results.Add(file);
			foreach (string dir in Directory.EnumerateDirectories(root))
				CollectFiles(dir, results);
		}
		catch
		{
			// 忽略无权限/系统子目录
		}
	}

	/// <summary>判断 <paramref name="directory"/> 是否位于 <paramref name="root"/> 之下（含自身）。</summary>
	private static bool IsUnder(string directory, string root)
	{
		try
		{
			string d = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			string r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			return d.Equals(r, StringComparison.OrdinalIgnoreCase)
				|| d.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	private bool _suspend; // 批量增删期间挂起预览重算

	private void AddPaths(IEnumerable<string> paths, bool fromFolder = false, string? root = null)
	{
		var existing = new HashSet<string>(Files.Select(f => f.FullPath), StringComparer.OrdinalIgnoreCase);
		bool added = false, merged = false;
		_suspend = true;
		try
		{
			// 先导入单个文件、后又导入其所在文件夹：自动合并为文件夹来源
			if (fromFolder && root is not null)
			{
				foreach (var f in Files)
				{
					if (!f.IsFolderSource && IsUnder(f.Directory, root))
					{
						f.IsFolderSource = true;
						f.SourceRoot = root;
						merged = true;
					}
				}
			}

			foreach (string p in paths)
			{
				if (existing.Add(p))
				{
					Files.Add(new FileItem(p)
					{
						IsFolderSource = fromFolder,
						SourceRoot = fromFolder ? root : null,
					});
					added = true;
				}
				else if (fromFolder && root is not null)
				{
					// 已存在但尚未标记为文件夹来源的条目，合并之
					var hit = Files.FirstOrDefault(f => string.Equals(f.FullPath, p, StringComparison.OrdinalIgnoreCase));
					if (hit is not null && !hit.IsFolderSource)
					{
						hit.IsFolderSource = true;
						hit.SourceRoot = root;
						merged = true;
					}
				}
			}
		}
		finally
		{
			_suspend = false;
		}
		if (!added && !merged) return;
		RefreshFilesView();
		RefreshFileStats();
		RecomputeAll();
	}

	/// <summary>
	/// 刷新文件列表：
	/// 文件夹来源的按根目录重新扫描为最新状态；文件来源的逐个刷新存在状态，不存在则标记失效。
	/// </summary>
	private void RefreshFromSources(bool folderScanOnly = false)
	{
		if (Files.Count == 0) return;

		var roots = Files.Where(f => f.IsFolderSource && !string.IsNullOrEmpty(f.SourceRoot))
			.Select(f => f.SourceRoot!)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (roots.Count == 0 && folderScanOnly) return;

		_suspend = true;
		try
		{
			// 汇总各文件夹根的最新文件集合
			var latest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (string root in roots)
			{
				List<string> paths = [];
				CollectFiles(root, paths);
				foreach (string p in paths) latest.TryAdd(p, root);
			}

			if (roots.Count > 0)
			{
				// 文件夹来源：逐个刷新存在状态；磁盘上已不存在的仅标记失效并保留，不清除报错条目
				foreach (var f in Files.Where(f => f.IsFolderSource).ToList())
					f.RefreshFromDisk();

				// 补充根目录下新出现的文件
				var present = new HashSet<string>(Files.Select(f => f.FullPath), StringComparer.OrdinalIgnoreCase);
				foreach (var (path, root) in latest)
					if (present.Add(path))
						Files.Add(new FileItem(path) { IsFolderSource = true, SourceRoot = root });
			}

			// 文件来源：逐个刷新（不存在的标记为失效并保留）
			if (!folderScanOnly)
			{
				foreach (var f in Files.Where(f => !f.IsFolderSource).ToList())
					f.RefreshFromDisk();
			}
		}
		finally
		{
			_suspend = false;
		}

		RefreshFilesView();
		RefreshFileStats();
		RecomputeAll();
	}

	// ─────────────── 规则 ───────────────

	private void AddRule(RuleType type)
	{
		Rules.Add(new RenameRule { Type = type, Config = RuleConfig.CreateDefault(type) });
	}

	private void DuplicateRule(RenameRule? rule)
	{
		if (rule is null) return;
		int idx = Rules.IndexOf(rule);
		var copy = new RenameRule { Type = rule.Type, Config = CloneConfig(rule.Config) };
		copy.Scope = rule.Scope;
		copy.Enabled = rule.Enabled;
		Rules.Insert(idx + 1, copy);
	}

	private static RuleConfig CloneConfig(RuleConfig src)
	{
		var copy = new RuleConfig { Type = src.Type };
		foreach (var p in typeof(RuleConfig).GetProperties())
		{
			if (p.CanWrite) p.SetValue(copy, p.GetValue(src));
		}
		return copy;
	}

	private void MoveUp(RenameRule? rule)
	{
		if (rule is null) return;
		int i = Rules.IndexOf(rule);
		if (i <= 0) return;
		Rules.Move(i, i - 1);
	}

	private void MoveDown(RenameRule? rule)
	{
		if (rule is null) return;
		int i = Rules.IndexOf(rule);
		if (i < 0 || i >= Rules.Count - 1) return;
		Rules.Move(i, i + 1);
	}

	// ─────────────── 选择 / 排序 / 过滤 ───────────────

	private void SetAllSelected(bool value)
	{
		// 失效条目不可选中，全选 / 全不选都跳过
		foreach (var f in Files.Where(f => f.IsVisible && !f.IsMissing)) f.Selected = value;
	}

	/// <summary>从列表中移除单个文件（文件列表行内的删除按钮；仅影响列表，不涉及磁盘文件）。</summary>
	private void RemoveFile(FileItem? file)
	{
		if (file is null) return;
		Files.Remove(file);
		RefreshFilesView();
		RefreshFileStats();
		RecomputeAll();
	}

	/// <summary>从列表中移除当前勾选的文件（仅影响列表，不涉及磁盘文件）。</summary>
	private void DeleteSelected()
	{
		var selected = Files.Where(f => f.Selected).ToList();
		if (selected.Count == 0) return;

		_suspend = true;
		try
		{
			foreach (var f in selected) Files.Remove(f);
		}
		finally
		{
			_suspend = false;
		}
		RefreshFilesView();
		RefreshFileStats();
		RecomputeAll();
	}

	private void Resort()
	{
		if (Files.Count < 2) return;
		var list = Files.ToList();
		list = SortMode switch
		{
			SortMode.NameAsc => [.. list.OrderBy(f => f.Name, NaturalStringComparer.Instance)],
			SortMode.NameDesc => [.. list.OrderByDescending(f => f.Name, NaturalStringComparer.Instance)],
			SortMode.ExtAsc => [.. list.OrderBy(f => f.Extension, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Name, NaturalStringComparer.Instance)],
			SortMode.ExtDesc => [.. list.OrderByDescending(f => f.Extension, StringComparer.OrdinalIgnoreCase).ThenByDescending(f => f.Name, NaturalStringComparer.Instance)],
			_ => list,
		};
		if (SortMode == SortMode.Import) return;
		_suspend = true;
		try
		{
			Files.Clear();
			foreach (var f in list) Files.Add(f);
		}
		finally
		{
			_suspend = false;
		}
		RefreshFilesView();
		RefreshFileStats();
		RecomputeAll();
	}

	private void ApplyFilter()
	{
		var spec = new FilterSpec { Value = FilterText };
		foreach (var f in Files) f.IsVisible = spec.Matches(f);
		RefreshFilesView();
		RefreshPreviewList();
		RaisePropertyChanged(nameof(SelectedText));
	}

	private void RefreshFilesView()
	{
		FilesView.Clear();
		foreach (var f in Files)
			if (f.IsVisible) FilesView.Add(f);
	}

	private void RefreshFileStats()
	{
		RaisePropertyChanged(nameof(FileTotalText));
		RaisePropertyChanged(nameof(SelectedText));
		RaisePropertyChanged(nameof(HasFiles));
		ClearCommand.RaiseCanExecuteChanged();
		DeleteSelectedCommand.RaiseCanExecuteChanged();
		RefreshCommand.RaiseCanExecuteChanged();
	}

	private bool IsInScope(FileItem f) => f.Selected; // FilesView 已保证可见

	// ─────────────── 预览 ───────────────

	public void RecomputeAll() => RefreshPreviewList();

	private void RefreshPreviewList()
	{
		var result = PreviewEngine.Compute(FilesView, Rules.Where(r => r.Enabled).ToList(), IsInScope);
		_affected = result.AffectedCount;
		_conflicts = result.ConflictCount;
		RaisePropertyChanged(nameof(Conflicts));
		RaisePropertyChanged(nameof(OkChangeCount));
		ExecuteCommand.RaiseCanExecuteChanged();

		List<PreviewItem> shown = _previewFilter switch
		{
			PreviewFilter.Affected => result.Items.Where(i => i.HasChange).ToList(),
			PreviewFilter.Conflict => result.Items.Where(i => i.Issue != PreviewIssue.None).ToList(),
			_ => result.Items.ToList(),
		};
		Previews.Clear();
		foreach (var item in shown) Previews.Add(item);
	}

	private void OnFilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.NewItems is not null)
		{
			foreach (FileItem item in e.NewItems)
				item.PropertyChanged += OnFilePropertyChanged;
		}
		if (e.OldItems is not null)
		{
			foreach (FileItem item in e.OldItems)
				item.PropertyChanged -= OnFilePropertyChanged;
		}
		if (_suspend) return; // 批量阶段结束由调用方统一刷新
		RefreshFilesView();
		RefreshFileStats();
		RecomputeAll();
	}

	private void OnFilePropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(FileItem.Selected))
		{
			RaisePropertyChanged(nameof(SelectedText));
			DeleteSelectedCommand.RaiseCanExecuteChanged();
			RecomputeAll();
		}
	}

	// ─────────────── 执行 / 撤销 ───────────────

	private void Execute()
	{
		if (!CanExecute) return;
		// 始终基于当前可见集构建执行计划，避免受预览过滤影响而漏执行
		var full = PreviewEngine.Compute(FilesView, Rules.Where(r => r.Enabled).ToList(), IsInScope);
		var plan = RenameService.GetPlannable(full.Items);
		if (plan.Count == 0) return;

		IsExecuting = true;
		Logs.Clear();
		ProgressTotal = plan.Count;
		ProgressCurrent = 0;
		try
		{
			// 先做文件有效性检测：已不存在（被删除 / 改名）的跳过改名，但不影响其它文件
			var runnable = new List<(FileItem File, string OldName, string NewName)>(plan.Count);
			var missing = new List<(FileItem File, string OldName, string NewName)>();
			foreach (var item in plan)
			{
				if (File.Exists(item.File.FullPath)) { item.File.IsMissing = false; runnable.Add(item); }
				else missing.Add(item);
			}

			foreach (var (file, oldName, newName) in missing)
			{
				file.Selected = false;   // 取消勾选
				file.IsMissing = true;   // 醒目失效标记
				Logs.Add(new LogItem
				{
					Original = oldName,
					NewName = newName,
					Status = "failed",
					Reason = "文件不存在（已被删除或改名）",
				});
			}

			int done = missing.Count;
			var outcomes = _service.Execute(runnable, _ =>
			{
				ProgressCurrent = ++done;
			});
			foreach (var o in outcomes)
			{
				Logs.Add(new LogItem
				{
					Original = o.OldName,
					NewName = o.NewFileName,
					Status = o.Success ? "success" : "failed",
					Reason = o.Success ? null : o.Error,
				});
			}
		}
		finally
		{
			IsExecuting = false;
			ProgressTotal = 0;
			ProgressCurrent = 0;
			RaisePropertyChanged(nameof(FileTotalText));
			// 文件夹来源重扫最新状态；文件来源已在改名时就地更新，报错条目保留。
			RefreshFromSources();
		}
	}

	private void Undo()
	{
		if (IsExecuting) return;
		var outcomes = _service.Undo();
		Logs.Clear();
		foreach (var o in outcomes)
		{
			Logs.Add(new LogItem
			{
				Original = o.OldName,
				NewName = o.NewFileName,
				Status = o.Success ? "success" : "failed",
				Reason = o.Success ? null : o.Error,
			});
		}
		RecomputeAll();
	}

	private void Redo()
	{
		if (IsExecuting) return;
		var outcomes = _service.Redo();
		Logs.Clear();
		foreach (var o in outcomes)
		{
			Logs.Add(new LogItem
			{
				Original = o.OldName,
				NewName = o.NewFileName,
				Status = o.Success ? "success" : "failed",
				Reason = o.Success ? null : o.Error,
			});
		}
		RecomputeAll();
	}

	private void ClearMissingFiles()
	{
		// 一次性清除所有失效条目（保留仍有效的文件）
		_suspend = true;
		try
		{
			foreach (var f in Files.Where(f => f.IsMissing).ToList())
				Files.Remove(f);
		}
		finally
		{
			_suspend = false;
		}
		RefreshFilesView();
		RefreshFileStats();
		RecomputeAll();
	}
}
