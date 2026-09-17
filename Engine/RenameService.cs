using System.IO;
using System.Runtime.InteropServices;
using RenameTool.Models;

namespace RenameTool.Engine;

/// <summary>改名失败的原因分类，用于界面按类型分别提示。</summary>
public enum RenameErrorKind { None, Conflict, InUse, Missing, Other }

/// <summary>磁盘改名操作结果。</summary>
public sealed record RenameOutcome(string OldPath, string NewPath, string OldName, string NewFileName,
	bool Success, string? Error, RenameErrorKind ErrorKind = RenameErrorKind.None);

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

	/// <summary>一次改名动作（仅路径层面）：把 <see cref="FromPath"/> 处的文件改成 <see cref="ToPath"/>。</summary>
	private readonly record struct Step(FileItem File, string FromPath, string FromName, string ToPath, string ToName);

	/// <summary>执行改名计划。</summary>
	/// <param name="plan">待执行项。</param>
	/// <param name="progress">每完成一项回调一次（传入目标名）。</param>
	/// <param name="token">取消令牌：请求后尽快停止，已完成的改名不自动回滚（由调用方决定是否撤销）。</param>
	/// <param name="applyRename">
	/// 改名成功后同步界面条目的回调。默认直接在调用线程更新；后台执行时由调用方传入一个
	/// 会切回 UI 线程的实现，避免跨线程修改绑定对象。
	/// </param>
	public List<RenameOutcome> Execute(IReadOnlyList<(FileItem File, string OldName, string NewName)> plan,
		Action<string>? progress = null,
		CancellationToken token = default,
		Action<FileItem, string>? applyRename = null)
	{
		var steps = new List<Step>(plan.Count);
		foreach (var (file, oldName, newName) in plan)
		{
			steps.Add(new Step(file,
				Path.Combine(file.Directory, oldName), oldName,
				Path.Combine(file.Directory, newName), newName));
		}

		var batch = new List<RenameOp>();
		var outcomes = new List<RenameOutcome>(steps.Count);
		foreach (var (step, outcome) in RunSteps(steps, progress, token, applyRename ?? DirectApplyRename))
		{
			outcomes.Add(outcome);
			if (outcome.Success)
				batch.Add(new RenameOp(step.File, step.FromPath, step.ToPath, step.FromName, step.ToName));
		}

		_redoStack.Clear();
		if (batch.Count > 0)
		{
			_undoStack.Add(batch);
			HistoryChanged?.Invoke();
		}
		return outcomes;
	}

	/// <summary>默认的界面同步方式：就地更新（仅适用于在 UI 线程上执行的情形）。</summary>
	private static readonly Action<FileItem, string> DirectApplyRename = static (file, name) => file.ApplyDiskRename(name);

	/// <summary>撤销最近一批成功改名（还原为原名）。</summary>
	public List<RenameOutcome> Undo()
	{
		if (_undoStack.Count == 0) return [];
		var batch = _undoStack[^1];
		_undoStack.RemoveAt(_undoStack.Count - 1);

		// 还原方向与执行方向相反，因此逆序构造步骤
		var steps = new List<Step>(batch.Count);
		for (int i = batch.Count - 1; i >= 0; i--)
		{
			var op = batch[i];
			steps.Add(new Step(op.File, op.NewPath, op.NewFileName, op.OldPath, op.OldName));
		}

		var byFile = new Dictionary<FileItem, RenameOp>(batch.Count);
		foreach (var op in batch) byFile[op.File] = op;

		var outcomes = new List<RenameOutcome>(steps.Count);
		var done = new List<RenameOp>();
		var pending = new List<RenameOp>();
		foreach (var (step, outcome) in RunSteps(steps, null, default, DirectApplyRename))
		{
			outcomes.Add(outcome);
			var op = byFile[step.File];
			if (outcome.Success) done.Add(op); else pending.Add(op);
		}

		// 逐条记录结果：已还原的进重做栈，没还原成的留在撤销栈顶等下次再试。
		// 旧实现要求“整批成功才出栈”，一旦有文件被占用，该批次会永久卡在栈顶——
		// 其中已还原的条目下轮只会以“源文件不存在”再次失败，等于撤销功能彻底锁死。
		if (done.Count > 0) _redoStack.Add(done);
		if (pending.Count > 0)
		{
			pending.Reverse(); // 还原为原始顺序，下次撤销仍按逆序处理
			_undoStack.Add(pending);
		}
		HistoryChanged?.Invoke();
		return outcomes;
	}

	/// <summary>重做最近一次被撤销的改名。</summary>
	public List<RenameOutcome> Redo()
	{
		if (_redoStack.Count == 0) return [];
		var batch = _redoStack[^1];
		_redoStack.RemoveAt(_redoStack.Count - 1);

		var steps = new List<Step>(batch.Count);
		foreach (var op in batch)
			steps.Add(new Step(op.File, op.OldPath, op.OldName, op.NewPath, op.NewFileName));

		var byFile = new Dictionary<FileItem, RenameOp>(batch.Count);
		foreach (var op in batch) byFile[op.File] = op;

		var outcomes = new List<RenameOutcome>(steps.Count);
		var done = new List<RenameOp>();
		var pending = new List<RenameOp>();
		foreach (var (step, outcome) in RunSteps(steps, null, default, DirectApplyRename))
		{
			outcomes.Add(outcome);
			var op = byFile[step.File];
			if (outcome.Success) done.Add(op); else pending.Add(op);
		}

		if (done.Count > 0) _undoStack.Add(done);
		if (pending.Count > 0) _redoStack.Add(pending);
		HistoryChanged?.Invoke();
		return outcomes;
	}

	/// <summary>带临时名（拆环）处理的批量改名：逐条执行 <paramref name="steps"/> 并返回结果。
	/// 请求取消后立即停止，剩余步骤不再产生结果（调用方据此判断“部分完成”）。</summary>
	private static List<(Step Step, RenameOutcome Outcome)> RunSteps(IReadOnlyList<Step> steps,
		Action<string>? progress, CancellationToken token, Action<FileItem, string> applyRename)
	{
		var actualFrom = new string[steps.Count];
		for (int i = 0; i < steps.Count; i++) actualFrom[i] = steps[i].FromPath;

		LiftToTempNames(steps, actualFrom, token);

		var results = new List<(Step, RenameOutcome)>(steps.Count);
		for (int i = 0; i < steps.Count; i++)
		{
			if (token.IsCancellationRequested) break;
			var step = steps[i];
			var outcome = DoMove(step.File, actualFrom[i], step.ToPath, step.FromName, step.ToName, token, applyRename);
			// 抬到临时名的文件若最终没能落位（例如目标被外部程序占用），立刻挪回原名，
			// 绝不留下 .rt-swap 残留；撤不回去才保持临时名，交由 DoMove 的报错提示用户。
			if (!outcome.Success && !string.Equals(actualFrom[i], step.FromPath, StringComparison.Ordinal)
				&& RestoreOne(actualFrom[i], step.FromPath))
			{
				outcome = outcome with { OldPath = step.FromPath };
				actualFrom[i] = step.FromPath;
			}
			results.Add((step, outcome));
			progress?.Invoke(step.ToName);
		}
		return results;
	}

	/// <summary>
	/// 处理交换名与改名环：形如 a→b、b→a 的一组改名无法逐条直接执行，因为目标路径此刻
	/// 还被本批中另一个待改名的文件占着。这里先把这些文件统一挪到临时名，把环拆开，
	/// 再由正常流程改成最终名。判定必须一次性基于改动前的磁盘状态完成，否则后判定的步骤
	/// 会看到已被腾空的路径而漏判。任一步挪动失败即整体放弃（把已挪的挪回原处），
	/// 退化为普通逐条改名，冲突交由 <see cref="DoMove"/> 逐条报出，绝不留下临时文件。
	/// </summary>
	private static void LiftToTempNames(IReadOnlyList<Step> steps, string[] actualFrom, CancellationToken token)
	{
		// 先找出本批里真正会挪窝的文件，只有它们才可能占着别人要落位的路径
		var movingByPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < steps.Count; i++)
		{
			var s = steps[i];
			if (string.Equals(s.FromPath, s.ToPath, StringComparison.Ordinal)) continue;
			if (NameUtils.SameName(s.FromPath, s.ToPath)) continue; // 仅大小写不同：DoMove 内部已用临时名处理
			movingByPath[s.FromPath] = i;
		}

		// 抬的是「占位的那一个」——此刻正躺在别人目标路径上、且自己也要挪窝的文件，
		// 而不是「等着落位的那一个」。链式映射（a→b、b→z）下占位者是 b，抬 a 只会让 b 继续挡路，
		// 于是 a 落不进 b、b 又已改走，最终留下 a 的临时文件。
		var needLift = new List<int>();
		var marked = new HashSet<int>();
		for (int i = 0; i < steps.Count; i++)
		{
			var s = steps[i];
			if (string.Equals(s.FromPath, s.ToPath, StringComparison.Ordinal)) continue;
			if (NameUtils.SameName(s.FromPath, s.ToPath)) continue;
			if (!movingByPath.TryGetValue(s.ToPath, out int occupant)) continue; // 目标不属于本批会挪窝的文件
			if (!File.Exists(s.ToPath)) continue;                                // 目标此刻就是空的，无需腾位
			if (marked.Add(occupant)) needLift.Add(occupant);
		}
		if (needLift.Count == 0) return;

		var lifted = new List<int>();
		foreach (int i in needLift)
		{
			// 取消时与拆环失败同样处理：把已挪走的挪回原处，绝不留下临时文件
			if (token.IsCancellationRequested) { RestoreLifted(steps, actualFrom, lifted); return; }
			string temp = steps[i].FromPath + TempSuffix;
			try
			{
				File.Move(steps[i].FromPath, temp);
				actualFrom[i] = temp;
				lifted.Add(i);
			}
			catch
			{
				// 拆环失败：整体退化为普通流程
				RestoreLifted(steps, actualFrom, lifted);
				return;
			}
		}
	}

	/// <summary>把已经挪到临时名的文件挪回原处（挪不回去的只能保持临时名，留待 DoMove 以“源文件不存在”报错）。</summary>
	private static void RestoreLifted(IReadOnlyList<Step> steps, string[] actualFrom, List<int> lifted)
	{
		for (int k = lifted.Count - 1; k >= 0; k--)
		{
			int j = lifted[k];
			if (RestoreOne(actualFrom[j], steps[j].FromPath)) actualFrom[j] = steps[j].FromPath;
		}
	}

	/// <summary>把单个临时名文件挪回原路径，成功返回 true（挪不回去返回 false，保持临时名）。</summary>
	private static bool RestoreOne(string from, string to)
	{
		try
		{
			File.Move(from, to);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private const string TempSuffix = ".rt-swap";

	private static RenameOutcome DoMove(FileItem file, string source, string target, string oldName, string newName,
		CancellationToken token, Action<FileItem, string> applyRename)
	{
		try
		{
			if (!File.Exists(source))
				return new RenameOutcome(source, target, oldName, newName, false, "源文件不存在", RenameErrorKind.Missing);
			if (string.Equals(source, target, StringComparison.Ordinal))
				return new RenameOutcome(source, target, oldName, newName, true, null);
			// 主动探测占用：以 FileShare.None 独占打开源文件，只要磁盘上还存在任何持有
			// 读写/删除权限的句柄，本次打开就会共享冲突。
			// 实测已知边界：Win11 记事本、照片这类程序是把文件整份读入内存后立即关闭句柄，
			// File.Move 与占用探测都看不到它（不是判据不严，而是磁盘上没有可判定的句柄）。
			// 这类“无句柄打开”无法识别，重命名会成功，占用方保存时会写回旧路径——
			// 已在评估后决定不做窗口标题猜测，理由见下方常量区的说明。
			if (TryFindHolders(source, out string[] holders, token))
			{
				string who = holders.Length > 0 ? $"（{string.Join("、", holders)}）" : string.Empty;
				return new RenameOutcome(source, target, oldName, newName, false,
					$"文件被占用：{Path.GetFileName(source)} 正被其它程序打开{who}，请关闭后重试", RenameErrorKind.InUse);
			}
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
				applyRename(file, Path.GetFileName(target));
				return new RenameOutcome(source, target, oldName, newName, true, null);
			}
			// Windows 下同名目录不会被 File.Exists 命中，直接 Move 会抛
			// UnauthorizedAccessException("Access to the path is denied.")，这里先行拦截并给出明确提示。
			if (Directory.Exists(target))
				return new RenameOutcome(source, target, oldName, newName, false, $"目标已存在同名文件夹：{Path.GetFileName(target)}", RenameErrorKind.Conflict);
			if (File.Exists(target))
				return new RenameOutcome(source, target, oldName, newName, false, $"目标已存在：{Path.GetFileName(target)}", RenameErrorKind.Conflict);
			File.Move(source, target);
			applyRename(file, Path.GetFileName(target));
			return new RenameOutcome(source, target, oldName, newName, true, null);
		}
		catch (UnauthorizedAccessException)
		{
			return new RenameOutcome(source, target, oldName, newName, false,
				$"拒绝访问：{Path.GetFileName(target)} 可能为只读或被其它程序占用", RenameErrorKind.InUse);
		}
		catch (IOException ex) when (IsFileInUse(ex))
		{
			return new RenameOutcome(source, target, oldName, newName, false,
				$"文件被占用：{Path.GetFileName(target)} 正被其它程序使用，请关闭后重试", RenameErrorKind.InUse);
		}
		catch (Exception ex)
		{
			return new RenameOutcome(source, target, oldName, newName, false, ex.Message, RenameErrorKind.Other);
		}
	}

	/// <summary>判断 IO 异常是否为“文件被其它进程占用 / 锁定”（ERROR_SHARING_VIOLATION / ERROR_LOCK_VIOLATION）。</summary>
	private static bool IsFileInUse(IOException ex)
	{
		int code = ex.HResult & 0xFFFF;
		return code is 32 or 33;
	}

	// ─────────────── 主动占用探测 ───────────────
	// 思路：以 FileShare.None 打开源文件探测既有句柄——只要其它进程还持有该文件的句柄，
	// 本次打开就会因共享模式冲突抛 ERROR_SHARING_VIOLATION（无论对方是否允许共享删除）。
	// 命中后再用 Restart Manager 查出占用进程名，拼进提示里方便用户定位。
	// 说明：不再做“窗口标题猜占用”的启发式判断。那套办法对不把文件名写进标题的程序一律失效，
	// 还会把标题里恰好含同名字符串的无关窗口误判成占用者，误伤远大于收益；
	// 真正打开的占用一律由上方的真实句柄探测与下方的 File.Move 异常兜住。
	// 实测（Win11）：记事本打开 probe.txt、图片程序打开 probe.png 时，
	// 窗口标题确实含文件名，但 FileShare.None 独占打开均成功 —— 即磁盘上不存在任何句柄。
	// 这类“读完即关句柄”的程序从原理上不可探测，故维持现状：不猜、不拦，如实报成功。

	private const int RmErrorSuccess = 0;
	private const int RmErrorMoreData = 234;
	private const int ProbeAttempts = 3;
	private const int ProbeRetryDelayMs = 25;

	[StructLayout(LayoutKind.Sequential)]
	private struct NativeFileTime
	{
		public uint Low;
		public uint High;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct RmUniqueProcess
	{
		public int ProcessId;
		public NativeFileTime StartTime;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct RmProcessInfo
	{
		public RmUniqueProcess Process;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string AppName;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string ServiceName;
		public int ApplicationType;
		public uint AppStatus;
		public uint SessionId;
		[MarshalAs(UnmanagedType.Bool)] public bool Restartable;
	}

	[DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
	private static extern int RmStartSession(out uint sessionHandle, int sessionFlags, string sessionKey);

	[DllImport("rstrtmgr.dll")]
	private static extern int RmEndSession(uint sessionHandle);

	[DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
	private static extern int RmRegisterResources(uint sessionHandle, uint fileCount, string[] fileNames,
		uint applicationCount, RmUniqueProcess[]? applications, uint serviceCount, string[]? serviceNames);

	[DllImport("rstrtmgr.dll")]
	private static extern int RmGetList(uint sessionHandle, out uint needed, ref uint count,
		[In, Out] RmProcessInfo[]? affectedApps, ref uint rebootReasons);

	/// <summary>
	/// 探测文件是否被其它进程打开：以文件句柄为准。
	/// 命中时 <paramref name="holders"/> 为占用进程名（可能为空）。
	/// </summary>
	private static bool TryFindHolders(string path, out string[] holders, CancellationToken token)
		=> HasForeignHandle(path, out holders, token);

	/// <summary>以 FileShare.None 探测其它进程是否持有该文件句柄；命中时附带占用进程名。</summary>
	private static bool HasForeignHandle(string path, out string[] holders, CancellationToken token)
	{
		holders = [];
		bool held = false;
		for (int attempt = 0; attempt < ProbeAttempts; attempt++)
		{
			try
			{
				// FileAccess.Read + FileShare.None：只要求读权限（只读文件也能测），
				// 但拒绝一切共享，因此任何既有句柄都会让本次打开失败。
				using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
				return false;
			}
			catch (IOException ex) when (IsFileInUse(ex))
			{
				held = true;
				// 重试间隔改为可取消等待：旧实现用 Thread.Sleep 硬等，在后台线程上无法响应中断
				if (attempt < ProbeAttempts - 1 && !WaitCancellable(ProbeRetryDelayMs, token)) return false;
			}
			catch (IOException) { return false; }                 // 其它 IO 问题交给 File.Move 报错
			catch (UnauthorizedAccessException) { return false; } // 权限问题交由后续流程处理
			catch (Exception) { return false; }
		}
		if (!held) return false;
		holders = QueryHolders(path);
		return true;
	}

	/// <summary>可取消的短等待：返回 false 表示等待期间已被取消。</summary>
	private static bool WaitCancellable(int milliseconds, CancellationToken token)
	{
		if (!token.CanBeCanceled)
		{
			Thread.Sleep(milliseconds);
			return true;
		}
		return !token.WaitHandle.WaitOne(milliseconds);
	}

	/// <summary>通过 Restart Manager 查出当前持有该文件的进程名（失败时返回空数组，不影响拦截结果）。</summary>
	private static string[] QueryHolders(string path)
	{
		uint session;
		if (RmStartSession(out session, 0, Guid.NewGuid().ToString("N")) != RmErrorSuccess)
			return [];
		try
		{
			string[] resources = [path];
			if (RmRegisterResources(session, 1, resources, 0, null, 0, null) != RmErrorSuccess)
				return [];

			uint needed = 0, count = 0, reasons = 0;
			if (RmGetList(session, out needed, ref count, null, ref reasons) != RmErrorMoreData || needed == 0)
				return [];

			var infos = new RmProcessInfo[needed];
			count = needed;
			if (RmGetList(session, out needed, ref count, infos, ref reasons) != RmErrorSuccess)
				return [];

			int self = Environment.ProcessId;
			var names = new List<string>();
			for (uint i = 0; i < count; i++)
			{
				if (infos[i].Process.ProcessId == self) continue;
				string name = infos[i].AppName;
				if (!string.IsNullOrWhiteSpace(name) &&
					!names.Contains(name, StringComparer.OrdinalIgnoreCase))
					names.Add(name);
			}
			return [.. names];
		}
		catch (DllNotFoundException) { return []; }
		catch (EntryPointNotFoundException) { return []; }
		finally { RmEndSession(session); }
	}
}

