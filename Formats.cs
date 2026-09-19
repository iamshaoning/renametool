namespace RenameTool;

public static class Formats
{
	/// <summary>字节数格式化为可读文本。</summary>
	public static string FormatFileSize(this long bytes)
	{
		if (bytes < 0) return "0 B";
		string[] units = ["B", "KB", "MB", "GB", "TB"];
		double value = bytes;
		int unit = 0;
		while (value >= 1024 && unit < units.Length - 1)
		{
			value /= 1024;
			unit++;
		}
		return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
	}
}

/// <summary>
/// 对话框 / 通知里的文本折行工具。
/// 目标是把一句话排成「各行宽度尽量接近」的若干行，避免出现
/// 「第一行塞满、第二行只剩两三个字」这种难看的排布；
/// 宽度以「全角字符 = 1 个单位（约等于 1 个字号）」估算，半角字符算半个单位，
/// 因此调用方只需把可用宽度除以字号即可得到每行单位数。
/// </summary>
public static class TextFlow
{
	/// <summary>单个字符占用的相对宽度（单位：全角字符宽度）。</summary>
	private static double Unit(char c) => c < 0x2E80 ? 0.5 : 1.0;

	private static double Width(string s, int start, int end)
	{
		double w = 0;
		for (int i = start; i < end; i++) w += Unit(s[i]);
		return w;
	}

	/// <summary>可在此字符之后断行（标点 / 空格后更自然）。</summary>
	private static bool IsBreakAfter(char c) =>
		c is '，' or '。' or '、' or '；' or '：' or '？' or '！'
			or ',' or '.' or ';' or ':' or '?' or '!' or ' ' or ')' or '）' or '》' or '」' or '』';

	/// <summary>中文排版里不该出现在行首的标点。</summary>
	private static bool IsLeadingPunct(char c) =>
		c is '，' or '。' or '、' or '；' or '：' or '？' or '！' or '）' or '》' or '」' or '』';

	/// <summary>
	/// 按每行 <paramref name="unitsPerLine"/> 个全角单位重排文本：
	/// 原本显式写的换行会保留为分段，过长的段落才做均衡折行。
	/// </summary>
	public static string Balance(string? text, double unitsPerLine)
	{
		if (string.IsNullOrEmpty(text)) return text ?? "";
		if (unitsPerLine < 2) return text;

		string[] paragraphs = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
		var lines = new List<string>(paragraphs.Length);
		foreach (string paragraph in paragraphs)
		{
			string p = paragraph.TrimEnd();
			if (p.Length == 0) { lines.Add(""); continue; }
			if (Width(p, 0, p.Length) <= unitsPerLine) { lines.Add(p); continue; }
			Wrap(p, unitsPerLine, lines);
		}
		return string.Join("\n", lines);
	}

	/// <summary>把一段过长的文本折成若干宽度接近的行，追加到 <paramref name="lines"/>。</summary>
	private static void Wrap(string p, double limit, List<string> lines)
	{
		double total = Width(p, 0, p.Length);
		// 至少两行；四舍五入向上取整，再留一点余量避免最后一行过短
		int lineCount = Math.Max(2, (int)Math.Ceiling(total / limit - 1e-6));
		int start = 0;
		double remaining = total;

		for (int line = 0; line < lineCount - 1 && start < p.Length; line++)
		{
			// 余下内容平均分给余下的行，得到本行的目标宽度
			double target = remaining / (lineCount - line);

			int cut = start;
			double bestDiff = double.MaxValue;
			double acc = 0;
			for (int i = start; i < p.Length; i++)
			{
				acc += Unit(p[i]);
				if (acc > limit) break;
				double diff = Math.Abs(acc - target);
				if (diff < bestDiff) { bestDiff = diff; cut = i; }
			}

			// 断点附近若有标点，优先断在标点之后，读起来更自然
			for (int i = Math.Min(cut, p.Length - 2); i >= start; i--)
			{
				if (!IsBreakAfter(p[i])) continue;
				if (Width(p, start, i + 1) < target * 0.6) break;
				cut = i;
				break;
			}

			// 别让下一行以标点开头
			int next = cut + 1;
			while (next < p.Length && IsLeadingPunct(p[next])
			       && Width(p, start, next + 1) <= limit + 1.5)
			{
				cut = next;
				next++;
			}

			string content = p[start..(cut + 1)].Trim();
			if (content.Length > 0) lines.Add(content);
			remaining -= Width(p, start, cut + 1);
			start = next;
		}

		if (start < p.Length)
		{
			string tail = p[start..].Trim();
			if (tail.Length > 0) lines.Add(tail);
		}
	}

	/// <summary>超长文本（例如系统异常信息、文件路径）截断到指定宽度并补省略号。</summary>
	public static string Shorten(string? text, double maxUnits)
	{
		if (string.IsNullOrEmpty(text)) return text ?? "";
		double acc = 0;
		for (int i = 0; i < text.Length; i++)
		{
			acc += Unit(text[i]);
			if (acc > maxUnits) return text[..Math.Max(1, i)] + "…";
		}
		return text;
	}
}
