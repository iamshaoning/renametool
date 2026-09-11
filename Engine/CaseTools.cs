using System.Text;

namespace RenameTool.Engine;

/// <summary>大小写变换工具。</summary>
public static class CaseTools
{
	/// <summary>文本置为指定大小写模式。所有模式都只改变字母大小写，不改变分隔符。</summary>
	public static string Apply(string text, Models.CaseMode mode)
	{
		if (string.IsNullOrEmpty(text)) return text;
		switch (mode)
		{
			case Models.CaseMode.UpperCase:
				return text.ToUpperInvariant();
			case Models.CaseMode.LowerCase:
				return text.ToLowerInvariant();
			case Models.CaseMode.TitleCase:
				return TitleCase(text);
			case Models.CaseMode.SentenceCase:
				return SentenceCase(text);
			default:
				return text;
		}
	}

	/// <summary>每个单词的首字母大写，其余字母转为小写（不改变分隔符）。</summary>
	private static string TitleCase(string text)
	{
		var sb = new StringBuilder(text.Length);
		bool wordStart = true;
		foreach (char c in text)
		{
			if (char.IsLetter(c))
			{
				sb.Append(wordStart ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
				wordStart = false;
			}
			else
			{
				sb.Append(c);
				if (!char.IsLetter(c) && !char.IsDigit(c)) wordStart = true;
			}
		}
		return sb.ToString();
	}

	/// <summary>句子：首字母大写，其后字母转小写。</summary>
	private static string SentenceCase(string text)
	{
		var sb = new StringBuilder(text.Length);
		bool firstLetter = true;
		foreach (char c in text)
		{
			if (char.IsLetter(c))
			{
				sb.Append(firstLetter ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
				firstLetter = false;
			}
			else
			{
				sb.Append(c);
			}
		}
		return sb.ToString();
	}

	/// <summary>
	/// 只把原文中指定的分隔符转换为目标分隔符：其他分隔符与字符一律原样保留，
	/// 连续的同种源分隔符折叠为一个目标分隔符。style 为 None 时返回原文。
	/// </summary>
	public static string ApplyStyle(string text, Models.WordStyle style)
	{
		(char from, char to) = style switch
		{
			Models.WordStyle.SpaceToDash => (' ', '-'),
			Models.WordStyle.SpaceToUnderscore => (' ', '_'),
			Models.WordStyle.DashToSpace => ('-', ' '),
			Models.WordStyle.UnderscoreToSpace => ('_', ' '),
			Models.WordStyle.DashToUnderscore => ('-', '_'),
			Models.WordStyle.UnderscoreToDash => ('_', '-'),
			_ => ('\0', '\0'),
		};
		if (from == '\0') return text;

		var sb = new StringBuilder(text.Length);
		bool prevFrom = false;
		foreach (char c in text)
		{
			if (c == from)
			{
				if (!prevFrom) sb.Append(to);
				prevFrom = true;
			}
			else
			{
				sb.Append(c);
				prevFrom = false;
			}
		}
		return sb.ToString();
	}
}
