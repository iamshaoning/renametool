using System.IO;
using RenameTool.Models;

namespace RenameTool.Engine;

/// <summary>预览计算：按规则链生成每个文件的新名称并做冲突检测。</summary>
public static class PreviewEngine
{
	public sealed record Result(List<PreviewItem> Items, int AffectedCount, int ConflictCount, int OkChangeCount);

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

		// 预计算每个启用「序号」规则的编号映射
		var sequenceMaps = new Dictionary<string, Dictionary<FileItem, string>>();
		foreach (var rule in enabledRules)
		{
			if (rule.Type == RuleType.Sequence)
				sequenceMaps[rule.Id] = SequenceMap.Build(rule, inScope);
		}

		var ordinalByFile = new Dictionary<FileItem, int>();
		for (int i = 0; i < inScope.Count; i++) ordinalByFile[inScope[i]] = i + 1;

		// 生成名称 + 冲突检测
		var items = new List<PreviewItem>(files.Count);
		var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		var rows = new List<(FileItem File, string NewName, bool InScope, string Dir)>(files.Count);

		foreach (var file in files)
		{
			bool inScopeFlag = isInScope(file);
			string original = file.Name;
			string newName = inScopeFlag
				? RuleEngine.ApplyChain(file, ordinalByFile.TryGetValue(file, out int o) ? o : 0,
					enabledRules, sequenceMaps)
				: original;

			string dirKey = file.Directory;
			string nameKey = dirKey.Length == 0 ? newName : dirKey + "\u0000" + newName;
			seen.TryGetValue(nameKey, out int count);
			seen[nameKey] = count + 1;

			rows.Add((file, newName, inScopeFlag, file.Directory));
		}

		int affected = 0, conflict = 0, okChanges = 0;
		string? prevDir = null;
		for (int i = 0; i < rows.Count; i++)
		{
			var (file, newName, inScopeFlag, dir) = rows[i];
			bool hasChange = !NameUtils.ExactName(file.Name, newName);
			var issue = PreviewIssue.None;

			if (inScopeFlag && hasChange)
			{
				affected++;
				string dirKey = dir.Length == 0 ? newName : dir + "\u0000" + newName;
				if (seen[dirKey] > 1) issue = PreviewIssue.Conflict;
				else if (newName.Length == 0 || newName == ".") issue = PreviewIssue.EmptyName;
				else if (NameUtils.HasIllegalChars(newName) || NameUtils.IsReservedName(newName)) issue = PreviewIssue.Illegal;

				if (issue == PreviewIssue.None) okChanges++;
				else conflict++;
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
				DirDisplay = dirDisplay,
			});
		}

		return new Result(items, affected, conflict, okChanges);
	}
}
