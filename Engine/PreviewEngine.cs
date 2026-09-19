using System.IO;
using RenameTool.Models;

namespace RenameTool.Engine;

/// <summary>预览计算：按规则链生成每个文件的新名称并做冲突检测。</summary>
public static class PreviewEngine
{
	public sealed record Result(List<PreviewItem> Items, int AffectedCount, int ConflictCount);

	/// <summary>
	/// 对 <paramref name="files"/>（保持列表顺序）计算全部预览。
	/// isInScope = true 的文件参与规则计算与冲突判定；为 false 的按原样展示（不参与执行）。
	/// </summary>
	public static Result Compute(
		IReadOnlyList<FileItem> files,
		IReadOnlyList<RenameRule> enabledRules,
		Func<FileItem, bool> isInScope)
	{
		var inScope = files.Where(isInScope).ToList();

		// 预计算每个启用「序号」规则的编号映射。
		// 起始值 + 步长累加越界的文件拿不到序号（见 SequenceMap.Build），单独收集后在预览里标出，
		// 否则它们会以「无变化」的样子混在列表里，用户不知道有文件被漏掉。
		var sequenceMaps = new Dictionary<string, Dictionary<FileItem, string>>();
		HashSet<FileItem>? seqOverflowed = null;
		foreach (var rule in enabledRules)
		{
			if (rule.Type != RuleType.Sequence) continue;
			var built = SequenceMap.Build(rule, inScope);
			sequenceMaps[rule.Id] = built.Map;
			if (built.Overflowed.Count > 0) (seqOverflowed ??= []).UnionWith(built.Overflowed);
		}

		var ordinalByFile = new Dictionary<FileItem, int>();
		for (int i = 0; i < inScope.Count; i++) ordinalByFile[inScope[i]] = i + 1;

		// 预计算每条「成对交换」规则的伙伴名称。交换不是逐文件能算出来的，必须知道伙伴进入本条规则时的名称；
		// 按规则链顺序构建，后一条交换规则的伙伴名里就自然带上了前一条交换规则的效果。
		var swapMaps = new Dictionary<string, Dictionary<FileItem, string>>();
		foreach (var rule in enabledRules)
		{
			if (rule.Type != RuleType.PairSwap) continue;

			var prefixRules = new List<RenameRule>();
			foreach (var r in enabledRules)
			{
				if (ReferenceEquals(r, rule)) break;
				prefixRules.Add(r);
			}

			var names = new Dictionary<FileItem, string>(inScope.Count);
			foreach (var f in inScope)
				names[f] = RuleEngine.ApplyChain(f, ordinalByFile[f], prefixRules, sequenceMaps, swapMaps);

			var map = new Dictionary<FileItem, string>(inScope.Count);
			int n = inScope.Count;
			for (int i = 0; i < n; i++)
			{
				int partner = rule.Config.SwapMode == PairSwapMode.Rotate
					? (i + 1) % n                             // 轮换：取下一个文件的名（最后一个取第一个）
					: i % 2 == 0 ? i + 1 : i - 1;             // 相邻两两：第 1↔2、3↔4……
				if (partner >= n) continue;                   // 奇数个文件时最后一个落单，保持原名
				map[inScope[i]] = names[inScope[partner]];
			}
			swapMaps[rule.Id] = map;
		}

		// 生成名称 + 冲突检测
		var items = new List<PreviewItem>(files.Count);
		var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		var rows = new List<(FileItem File, string OriginalName, string NewName, bool InScope, string Dir)>(files.Count);

		foreach (var file in files)
		{
			bool inScopeFlag = isInScope(file);
			string original = file.Name;
			string newName;
			if (!inScopeFlag) newName = original;
			// 手动锁定的目标名优先于规则计算结果，锁定期间不随规则变化
			else if (file.LockedName is { } locked) newName = locked;
			else newName = RuleEngine.ApplyChain(file,
				ordinalByFile.TryGetValue(file, out int o) ? o : 0, enabledRules, sequenceMaps, swapMaps);

			string nameKey = TargetKey(file.Directory, newName);
			seen.TryGetValue(nameKey, out int count);
			seen[nameKey] = count + 1;

			rows.Add((file, original, newName, inScopeFlag, file.Directory));
		}

		int affected = 0, conflict = 0;
		string? prevDir = null;

		// 先算出「本批会改名挪走哪些现有路径」，再做冲突判定：交换名（a→b、b→a）与改名环里，
		// 目标名此刻确实存在于磁盘上，但占用者本人也在本批且会被挪走，因此不该报冲突。
		var vacating = VacatingPaths(rows.Select(r => (r.Dir, r.OriginalName, r.NewName, r.InScope)));

		for (int i = 0; i < rows.Count; i++)
		{
			var (file, original, newName, inScopeFlag, dir) = rows[i];
			bool hasChange = !NameUtils.ExactName(original, newName);
			var issue = PreviewIssue.None;

			if (inScopeFlag && hasChange)
			{
				affected++;
				// 判定顺序 = 「越具体、越硬性」的排在前：空名 / 非法字符 / 路径过长都是必须先解决的问题，
				// 若让「重名」抢先判定，用户会看到「冲突」而去查重名，真正的根因被掩盖。
				if (newName.Length == 0 || newName == ".") issue = PreviewIssue.EmptyName;
				else if (NameUtils.HasIllegalChars(newName) || NameUtils.IsReservedName(newName)) issue = PreviewIssue.Illegal;
				else if (dir.Length + 1 + newName.Length >= NameUtils.MaxFullPathLength) issue = PreviewIssue.TooLong;
				else if (seen[TargetKey(dir, newName)] > 1) issue = PreviewIssue.Conflict;
				// 与执行期同一套判定：目标被磁盘占用（且占用者不在本批挪走名单里）才算冲突
				else if (TakenOnDisk(dir, original, newName, vacating)) issue = PreviewIssue.Conflict;

				if (issue != PreviewIssue.None) conflict++;
			}

			// 序号越界：该文件拿不到序号、不会按原意改名。若此刻没有更具体的问题，就标出它并计入统计，
			// 执行期会被 BuildPlan 跳过并写进日志——不能让它静默留在列表里当作「无变化」。
			if (inScopeFlag && issue == PreviewIssue.None
				&& seqOverflowed is not null && seqOverflowed.Contains(file))
			{
				if (!hasChange) affected++;
				conflict++;
				issue = PreviewIssue.SequenceOverflow;
			}

			// 文件夹名行按目录分组固定在“该目录首个文件”处：是否勾选都参与分组，
			// 这样取消勾选某个文件时它只会置灰，不会带着文件夹名行一起移位。
			string dirDisplay = "";
			if (dir != prevDir)
			{
				dirDisplay = Path.GetFileName(dir) ?? dir;
				prevDir = dir;
			}

			items.Add(new PreviewItem
			{
				File = file,
				OriginalName = file.Name,
				NewName = newName,
				Issue = issue,
				IsInScope = inScopeFlag,
				IsLocked = file.LockedName is not null,
				DirDisplay = dirDisplay,
			});
		}

		return new Result(items, affected, conflict);
	}

	/// <summary>执行前体检 + 计划构建结果。</summary>
	/// <param name="ConflictCount">体检中命中的命名冲突条数（与预览阶段的计数口径一致，均以磁盘实际状态为准）。</param>
	public sealed record PlanResult(
		List<(FileItem File, string OldName, string NewName)> Plan,
		List<(string OldName, string NewName, string Reason)> Skipped,
		bool Aborted,
		int ConflictCount);

	/// <summary>
	/// 执行前体检：在预览结果基础上进一步核对磁盘状态（目标是否已存在），
	/// 并按 <paramref name="policy"/> 处理命名冲突，产出最终可执行计划。
	/// 硬性问题（空名/非法字符/路径过长）一律跳过并给出原因。
	/// </summary>
	public static PlanResult BuildPlan(IReadOnlyList<PreviewItem> items, ConflictPolicy policy)
	{
		var plan = new List<(FileItem File, string OldName, string NewName)>();
		var skipped = new List<(string OldName, string NewName, string Reason)>();
		int conflicts = 0;

		// 同批内已占用的目标名（键：目录\0名称，大小写不敏感）
		var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		// 与预览阶段共用同一套「本批会挪走哪些路径」判定，两边结论一致
		var vacating = VacatingPaths(items.Select(i => (i.File.Directory, i.OriginalName, i.NewName, i.IsInScope)));

		foreach (var item in items)
		{
			if (!item.IsInScope) continue;

			// 硬性问题一律跳过并说明原因。此判定放在「有无变化」之前：序号越界的文件保持原名、
			// HasChange 为 false，若先按无变化略过，它就永远不会出现在日志里。
			if (item.Issue is PreviewIssue.EmptyName or PreviewIssue.Illegal
				or PreviewIssue.TooLong or PreviewIssue.SequenceOverflow)
			{
				skipped.Add((item.OriginalName, item.NewName, item.IssueText));
				continue;
			}

			if (!item.HasChange) continue;

			string dir = item.File.Directory;
			string name = item.NewName;

			bool takenInBatch = used.Contains(TargetKey(dir, name));
			// 磁盘占用同时考虑同名文件与同名文件夹：后者 File.Exists 不命中，若漏判会在执行阶段抛
			// "Access to the path is denied."，因此这里一并纳入体检。
			// 占用者若本身就在本批且会被改名挪走（交换名 a→b / b→a、改名环），则不算冲突：
			// RenameService 会先把这些条目抬到临时名再落位，本来就能成功。
			// 注意：此处刻意不因为「Issue 不为 None」就跳过磁盘核对。预览阶段被判为「同目录同名」
			// 的条目（例如目标名正好与本批中某个不需要改名的文件同名）同样要走这一关，否则 Abort
			// 策略不会命中，条目会带着冲突进入执行期才失败（表现为「已取消执行」的提示永远不触发）。
			bool takenOnDisk = TakenOnDisk(dir, item.OriginalName, name, vacating);

			if (takenInBatch || takenOnDisk)
			{
				if (policy == ConflictPolicy.Abort)
				{
					// 中止策略下不立刻返回：先把整批冲突数数完，
					// 界面才能报出真实条数（预览期只统计批内重名，磁盘已存在的目标不在其中）。
					used.Add(TargetKey(dir, name));
					conflicts++;
					continue;
				}

				if (policy == ConflictPolicy.Skip)
				{
					skipped.Add((item.OriginalName, name, takenOnDisk ? "目标已存在" : "与其他文件重名"));
					continue;
				}

				// 自动加序号：避开同批占用与磁盘已有文件（与自身同名、占用者本批会挪走的不算占用）
				string unique = NameUtils.MakeUnique(name, candidate =>
					used.Contains(TargetKey(dir, candidate))
					|| TakenOnDisk(dir, item.OriginalName, candidate, vacating));
				name = unique;
			}

			used.Add(TargetKey(dir, name));

			// 体检：自动编号后必须整套复核，不能只查长度。
			// MakeUnique 只在基名后追加 “ (1)”，既不修非法字符也不改保留名；若这里漏查，
			// 预览会说「能解决」，执行期才抛异常，用户看到的失败与预览不一致。
			if (name.Length == 0 || name == ".")
			{
				skipped.Add((item.OriginalName, name, "名称为空"));
				continue;
			}
			if (NameUtils.HasIllegalChars(name) || NameUtils.IsReservedName(name))
			{
				skipped.Add((item.OriginalName, name, "含非法字符或为系统保留名"));
				continue;
			}
			if (dir.Length + 1 + name.Length >= NameUtils.MaxFullPathLength)
			{
				skipped.Add((item.OriginalName, name, "路径过长"));
				continue;
			}

			plan.Add((item.File, item.OriginalName, name));
		}

		if (policy == ConflictPolicy.Abort && conflicts > 0)
			return new PlanResult([], [], true, conflicts);

		return new PlanResult(plan, skipped, false, conflicts);
	}

	/// <summary>
	/// 本批中「会被改名挪走」的现有路径集合（键：目录\0现名，大小写不敏感）。
	/// 交换名（a→b、b→a）与改名环里，目标名此刻确实存在于磁盘，但占用者本人也在本批、且会被改名挪走，
	/// 因此不构成冲突；执行期 <see cref="RenameService"/> 会先把所有条目抬到临时名再落位，与此判定配套。
	/// 硬性问题条目（空名 / 非法字符 / 路径过长）不会真正执行改名，故不腾出位置。
	/// 局限：策略为「跳过冲突项」时，若某个占用者自身因冲突被跳过，这里仍认为它会挪走，
	/// 该级联场景留到执行期按单项失败报出。
	/// </summary>
	public static HashSet<string> VacatingPaths(
		IEnumerable<(string Dir, string OriginalName, string NewName, bool InScope)> rows)
	{
		var vacating = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var (dir, original, target, inScope) in rows)
		{
			if (!inScope) continue;
			if (NameUtils.ExactName(original, target)) continue;         // 不改名 → 不腾出位置
			if (target.Length == 0 || target == ".") continue;
			if (NameUtils.HasIllegalChars(target) || NameUtils.IsReservedName(target)) continue;
			if (dir.Length + 1 + target.Length >= NameUtils.MaxFullPathLength) continue;
			vacating.Add(TargetKey(dir, original));
		}
		return vacating;
	}

	/// <summary>目标路径此刻是否已被磁盘占用（与自身同名、占用者本批会挪走的不算占用）。</summary>
	private static bool TakenOnDisk(string dir, string originalName, string targetName, HashSet<string> vacating)
	{
		if (NameUtils.SameName(targetName, originalName)) return false;
		if (vacating.Contains(TargetKey(dir, targetName))) return false;
		string path = Path.Combine(dir, targetName);
		return File.Exists(path) || Directory.Exists(path);
	}

	private static string TargetKey(string dir, string name) => dir.Length == 0 ? name : dir + "\u0000" + name;
}
