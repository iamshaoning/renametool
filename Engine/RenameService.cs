using System.IO;
using RenameTool.Models;

namespace RenameTool.Engine;

/// <summary>磁盘改名操作结果。</summary>
public sealed record RenameOutcome(string OldPath, string NewPath, string OldName, string NewFileName,
	bool Success, string? Error);

/// <summary>一条成功执行的改名记录（携带文件项，用于界面同步与撤销还原）。</summary>
public sealed record RenameOp(FileItem File, string OldPath, string NewPath, string OldName, string NewFileName);

/// <summary>
/// 执行与撤销/重做服务，以“批”为单位维护双栈。
/// 单文件失败不影响其它文件继续；撤销只还原成功项。
/// </summary>
public sealed class RenameService
{
	private readonly List<List<RenameOp>> _undoStack = [];
	private readonly List<List<RenameOp>> _redoStack = [];

	public bool CanUndo => _undoStack.Count > 0;
	public bool CanRedo => _redoStack.Count > 0;

	public event Action? HistoryChanged;

	public void ClearHistory()
	{
		_undoStack.Clear();
		_redoStack.Clear();
		HistoryChanged?.Invoke();
	}

	/// <summary>从预览结果提取可执行的改名计划。</summary>
	public static List<(FileItem File, string OldName, string NewName)> GetPlannable(
		IEnumerable<PreviewItem> previews) =>
		previews.Where(p => p.CanRename)
			.Select(p => (p.File, p.File.Name, p.NewName))
			.ToList();

	/// <summary>执行改名计划。</summary>
	public List<RenameOutcome> Execute(IReadOnlyList<(FileItem File, string OldName, string NewName)> plan,
		Action<string>? progress = null)
	{
		var outcomes = new List<RenameOutcome>(plan.Count);
		var batch = new List<RenameOp>();
		foreach (var (file, oldName, newName) in plan)
		{
			string source = Path.Combine(file.Directory, oldName);
			string target = Path.Combine(file.Directory, newName);
			var outcome = DoMove(file, source, target, oldName, newName);
			outcomes.Add(outcome);
			if (outcome.Success)
				batch.Add(new RenameOp(file, source, target, oldName, newName));
			progress?.Invoke(newName);
		}

		_redoStack.Clear();
		if (batch.Count > 0)
		{
			_undoStack.Add(batch);
			HistoryChanged?.Invoke();
		}
		return outcomes;
	}

	/// <summary>撤销最近一批成功改名（还原为原名）。</summary>
	public List<RenameOutcome> Undo()
	{
		if (_undoStack.Count == 0) return [];
		var batch = _undoStack[^1];
		var outcomes = new List<RenameOutcome>();
		bool allOk = true;
		for (int i = batch.Count - 1; i >= 0; i--)
		{
			var op = batch[i];
			var outcome = DoMove(op.File, op.NewPath, op.OldPath, op.NewFileName, op.OldName);
			outcomes.Add(outcome);
			if (!outcome.Success) allOk = false;
		}
		if (allOk)
		{
			_undoStack.RemoveAt(_undoStack.Count - 1);
			_redoStack.Add(batch);
		}
		HistoryChanged?.Invoke();
		return outcomes;
	}

	/// <summary>重做最近一次被撤销的改名。</summary>
	public List<RenameOutcome> Redo()
	{
		if (_redoStack.Count == 0) return [];
		var batch = _redoStack[^1];
		var outcomes = new List<RenameOutcome>();
		bool allOk = true;
		foreach (var op in batch)
		{
			var outcome = DoMove(op.File, op.OldPath, op.NewPath, op.OldName, op.NewFileName);
			outcomes.Add(outcome);
			if (!outcome.Success) allOk = false;
		}
		if (allOk)
		{
			_redoStack.RemoveAt(_redoStack.Count - 1);
			_undoStack.Add(batch);
		}
		HistoryChanged?.Invoke();
		return outcomes;
	}

	private static RenameOutcome DoMove(FileItem file, string source, string target, string oldName, string newName)
	{
		try
		{
			if (!File.Exists(source))
				return new RenameOutcome(source, target, oldName, newName, false, "源文件不存在");
			if (string.Equals(source, target, StringComparison.Ordinal))
				return new RenameOutcome(source, target, oldName, newName, true, null);
			if (NameUtils.SameName(source, target))
			{
				// 仅大小写不同：Windows 文件名大小写不敏感，需先改成临时名再改成目标名。
				string temp = source + ".rt-tmp";
				File.Move(source, temp);
				try
				{
					File.Move(temp, target);
				}
				catch
				{
					// 第二步失败时把临时名改回原名，避免文件残留为 .rt-tmp
					try { File.Move(temp, source); } catch { /* 回滚失败则保持原样 */ }
					throw;
				}
				file.ApplyDiskRename(Path.GetFileName(target));
				return new RenameOutcome(source, target, oldName, newName, true, null);
			}
			if (File.Exists(target))
				return new RenameOutcome(source, target, oldName, newName, false, $"目标已存在：{Path.GetFileName(target)}");
			File.Move(source, target);
			file.ApplyDiskRename(Path.GetFileName(target));
			return new RenameOutcome(source, target, oldName, newName, true, null);
		}
		catch (Exception ex)
		{
			return new RenameOutcome(source, target, oldName, newName, false, ex.Message);
		}
	}
}
