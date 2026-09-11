using RenameTool.Models;

namespace RenameTool.Engine;

/// <summary>为单个“序号”规则预计算每个文件的序列号文本。</summary>
public static class SequenceMap
{
	/// <summary>按作用范围分组，并按规则要求排序后生成序号。</summary>
	public static Dictionary<FileItem, string> Build(RenameRule rule, IReadOnlyList<FileItem> activeFiles)
	{
		var cfg = rule.Config;
		var result = new Dictionary<FileItem, string>();

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
		}

		foreach (var group in groups)
		{
			IReadOnlyList<FileItem> ordered = group;
			if (cfg.SortBeforeNumbering && cfg.SortBy != SeqSortBy.ListOrder)
			{
				var comparer = NaturalOrString(cfg);
				ordered = (cfg.SortAscending
						? group.OrderBy(f => SortValue(cfg.SortBy, f), comparer)
						: group.OrderByDescending(f => SortValue(cfg.SortBy, f), comparer))
					.ToList();
			}

			long value = cfg.Start;
			foreach (var f in ordered)
			{
				result[f] = SeqNumber.Format(value, cfg.SeqType, cfg.Padding);
				value += cfg.Step;
			}
		}
		return result;
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
