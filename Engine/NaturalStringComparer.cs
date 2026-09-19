namespace RenameTool.Engine;

/// <summary>自然排序：将内嵌数字按数值比较，如 "file2" &lt; "file10"。</summary>
public sealed class NaturalStringComparer : IComparer<string>
{
	public static readonly NaturalStringComparer Instance = new();

	public int Compare(string? x, string? y)
	{
		if (ReferenceEquals(x, y)) return 0;
		if (x is null) return -1;
		if (y is null) return 1;

		int i = 0, j = 0;
		while (i < x.Length && j < y.Length)
		{
			char cx = x[i], cy = y[j];
			if (char.IsDigit(cx) && char.IsDigit(cy))
			{
				int si = i, sj = j;
				while (i < x.Length && char.IsDigit(x[i])) i++;
				while (j < y.Length && char.IsDigit(y[j])) j++;
				// 去掉前导零
				int a0 = SkipZeros(x, si, i);
				int b0 = SkipZeros(y, sj, j);
				int la = i - a0, lb = j - b0;
				if (la != lb) return la < lb ? -1 : 1;
				for (int k = 0; k < la; k++)
				{
					char da = x[a0 + k], db = y[b0 + k];
					if (da != db) return da < db ? -1 : 1;
				}
				// 数值相等（如 "1" 与 "01"）时用原始位段长度打破平局。否则两者被判为相等，
				// 排序结果依赖输入顺序，"先排序再编号" 会对同一份列表给出不同编号。
				int ra = i - si, rb = j - sj;
				if (ra != rb) return ra < rb ? -1 : 1;
				continue;
			}
			if (cx != cy)
				return char.ToUpperInvariant(cx) < char.ToUpperInvariant(cy) ? -1 : 1;
			i++;
			j++;
		}
		return (x.Length - i).CompareTo(y.Length - j);
	}

	private static int SkipZeros(string s, int from, int to)
	{
		while (from < to - 1 && s[from] == '0') from++;
		return from;
	}
}
