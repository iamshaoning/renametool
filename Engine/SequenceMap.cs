using RenameTool.Models;

namespace RenameTool.Engine;

/// <summary>为单个“序号”规则预计算每个文件的序列号文本。</summary>
public static class SequenceMap
{
	/// <summary>单条序号规则的预计算结果。</summary>
	/// <param name="Map">文件 → 序号文本。</param>
	/// <param name="Overflowed">起始值 + 步长累加越出 <see cref="long"/> 范围、拿不到序号的文件。
	/// 这些文件不参与改名（保持原名），并在预览里标为「序号越界」。</param>
	public sealed record BuildResult(Dictionary<FileItem, string> Map, HashSet<FileItem> Overflowed);

	/// <summary>按作用范围分组，并按规则要求排序后生成序号。</summary>
	public static BuildResult Build(RenameRule rule, IReadOnlyList<FileItem> activeFiles)
	{
		var cfg = rule.Config;
		var result = new Dictionary<FileItem, string>();
		var overflowed = new HashSet<FileItem>();

		var groups = new List<List<FileItem>>();
		switch (cfg.Scope)
		{
			case SeqScope.Global:
				groups.Add([.. activeFiles]);
				break;
			case SeqScope.PerFolder:
				groups.AddRange(activeFiles.GroupBy(f => f.Directory, StringComparer.OrdinalIgnoreCase)
					.Select(g => g.ToList()));
				break;
			case SeqScope.PerExtension:
				groups.AddRange(activeFiles.GroupBy(f => f.Extension, StringComparer.OrdinalIgnoreCase)
					.Select(g => g.ToList()));
				break;
			default:
				// 非法枚举值（例如手工编辑后的配置文件）按全局处理，避免整个序号规则静默失效
				groups.Add([.. activeFiles]);
				break;
		}

		foreach (var group in groups)
		{
			IReadOnlyList<FileItem> ordered = group;
			if (cfg.SortBeforeNumbering)
			{
				if (cfg.SortBy == SeqSortBy.ListOrder)
				{
					// 「当前列表顺序」同样支持反向：取消「升序」即把列表倒过来编号。
					ordered = cfg.SortAscending ? group : [.. group.AsEnumerable().Reverse()];
				}
				else
				{
					var comparer = NaturalOrString(cfg);
					ordered = (cfg.SortAscending
							? group.OrderBy(f => SortValue(cfg.SortBy, f), comparer)
							: group.OrderByDescending(f => SortValue(cfg.SortBy, f), comparer))
						.ToList();
				}
			}

			long value = cfg.Start;
			bool overflow = false;
			foreach (var f in ordered)
			{
				if (overflow)
				{
					overflowed.Add(f);
					continue;
				}
				result[f] = SeqNumber.Format(value, cfg.SeqType, cfg.Padding);
				// 起始值与步长均由用户自由输入，累加越界会回绕成负数（例如 -9223372036854775808）。
				// 这里既不回绕也不夹住：夹住会让边界之后的文件拿到重复序号，静默产出错误名字。
				// 改为从越界处起把该组剩余文件全部记为「序号越界」，它们不参与改名并在预览里明确标出。
				if (cfg.Step > 0 && value > long.MaxValue - cfg.Step) overflow = true;
				else if (cfg.Step < 0 && value < long.MinValue - cfg.Step) overflow = true;
				else value += cfg.Step;
			}
		}
		return new BuildResult(result, overflowed);
	}

	private static string SortValue(SeqSortBy by, FileItem f) => by switch
	{
		SeqSortBy.Size => f.Size.ToString("D20"),
		SeqSortBy.Modified => f.Modified.Ticks.ToString("D20"),
		SeqSortBy.Extension => f.Extension,
		_ => f.Name,
	};

	private static IComparer<string> NaturalOrString(RuleConfig cfg)
		=> cfg.NaturalSort ? NaturalStringComparer.Instance : StringComparer.OrdinalIgnoreCase;
}
