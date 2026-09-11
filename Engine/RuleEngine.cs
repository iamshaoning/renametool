using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using RenameTool.Models;

namespace RenameTool.Engine;

/// <summary>
/// 规则执行引擎：按规则链顺序对每个文件逐个应用，前一条输出作为后一条输入。
/// 支持作用域（文件名/扩展名/全名）与 {变量} 模板。
/// </summary>
public static class RuleEngine
{
	// ────────────────────────── 主链 ──────────────────────────

	/// <summary>对单个文件应用整条启用的规则链，返回最终名称。</summary>
	/// <param name="file">文件</param>
	/// <param name="ordinal">该文件在激活列表中的序号（从 1 起，供 {n} 变量）</param>
	/// <param name="sequenceMaps">序号规则预计算结果：ruleId → 文件 → 序号文本</param>
	public static string ApplyChain(FileItem file, int ordinal, IReadOnlyList<RenameRule> rules,
		IReadOnlyDictionary<string, Dictionary<FileItem, string>> sequenceMaps)
	{
		string current = file.Name;
		foreach (var rule in rules)
		{
			if (!rule.Enabled) continue;
			if (rule.Type == RuleType.Sequence)
			{
				var map = sequenceMaps.TryGetValue(rule.Id, out var m) ? m : null;
				string token = map is not null && map.TryGetValue(file, out var t) ? t : "";
				current = ApplySequence(rule, current, file, token, ordinal);
			}
			else
			{
				current = ApplyTransform(rule, current, file, ordinal);
			}
		}
		return current;
	}

	// ────────────────────────── 作用域与各规则 ──────────────────────────

	private static string ApplyTransform(RenameRule rule, string name, FileItem file, int ordinal)
	{
		return TransformByScope(rule.Scope, name, target =>
		{
			return rule.Type switch
			{
				RuleType.FindReplace => FindReplace(rule.Config, target, file, ordinal),
				RuleType.Insert => Insert(rule.Config, target, file, ordinal),
				RuleType.Sequence => target,
				RuleType.NameTemplate => NameTemplate(rule.Config, target, file, ordinal),
				RuleType.CaseStyle => Case(rule.Config, target),
				RuleType.RemoveCleanup => Remove(rule.Config, target),
				_ => target,
			};
		});
	}

	private static string ApplySequence(RenameRule rule, string name, FileItem file, string token, int ordinal)
	{
		var cfg = rule.Config;
		if (token.Length == 0) return name;

		// 在作用域文本前/后添加或整体替换（前/后缀时插入分隔符）
		return TransformByScope(rule.Scope, name, target =>
			cfg.SeqPosition switch
			{
				SeqPosition.Start => cfg.Separator.Length == 0 ? token + target : token + cfg.Separator + target,
				SeqPosition.End => cfg.Separator.Length == 0 ? target + token : target + cfg.Separator + token,
				_ => token,
			});
	}

	/// <summary>按作用域对名称的某部分做变换并重组。</summary>
	private static string TransformByScope(ExtensionScope scope, string name, Func<string, string> fn)
	{
		var (baseName, ext) = NameUtils.Split(name);
		switch (scope)
		{
			case ExtensionScope.Name:
				return NameUtils.Join(fn(baseName), ext);
			case ExtensionScope.Extension:
				if (ext.Length <= 1) return name; // 无扩展名
				string newExt = fn(ext[1..]);
				return newExt.Length == 0 ? baseName : baseName + "." + newExt;
			default:
				return fn(name);
		}
	}

	// ────────────────────────── 各规则实现 ──────────────────────────

	private static string FindReplace(RuleConfig cfg, string text, FileItem file, int ordinal)
	{
		if (string.IsNullOrEmpty(cfg.Find)) return text;
		string replacement = ResolveTemplate(cfg.Replace, file, text, ordinal);

		if (cfg.UseRegex)
		{
			try
			{
				var options = RegexOptions.None;
				if (!cfg.CaseSensitive) options |= RegexOptions.IgnoreCase;
				var rx = new Regex(cfg.Find, options);
				return cfg.MatchAll
					? rx.Replace(text, replacement)
					: rx.Replace(text, replacement, 1);
			}
			catch
			{
				return text; // 非法正则：静默跳过，由 UI 校验提示
			}
		}

		var comparison = cfg.CaseSensitive
			? StringComparison.Ordinal
			: StringComparison.OrdinalIgnoreCase;

		if (cfg.MatchAll)
			return text.Replace(cfg.Find, replacement, comparison);

		int idx = text.IndexOf(cfg.Find, comparison);
		return idx < 0 ? text : text[..idx] + replacement + text[(idx + cfg.Find.Length)..];
	}

	private static string NameTemplate(RuleConfig cfg, string text, FileItem file, int ordinal)
	{
		if (string.IsNullOrEmpty(cfg.Template)) return text;
		return ResolveTemplate(cfg.Template, file, text, ordinal);
	}

	private static string Insert(RuleConfig cfg, string text, FileItem file, int ordinal)
	{
		string toInsert = ResolveTemplate(cfg.Text, file, text, ordinal);
		if (toInsert.Length == 0) return text;
		return cfg.InsertAt switch
		{
			InsertPosition.Start => toInsert + text,
			InsertPosition.End => text + toInsert,
			_ => InsertAtIndex(text, toInsert, cfg.InsertIndex),
		};
	}

	private static string InsertAtIndex(string text, string insert, int index)
	{
		if (index < 0) index = 0;
		if (index > text.Length) index = text.Length;
		return text[..index] + insert + text[index..];
	}

	private static string Case(RuleConfig cfg, string text)
	{
		string result = text;
		if (cfg.WordStyle != WordStyle.None)
			result = CaseTools.ApplyStyle(result, cfg.WordStyle);
		if (cfg.CaseMode != CaseMode.None)
			result = CaseTools.Apply(result, cfg.CaseMode);
		return result;
	}

	private static string Remove(RuleConfig cfg, string text)
	{
		switch (cfg.CleanupMode)
		{
			case CleanupMode.Chars:
			{
				int count = Math.Min(Math.Max(0, cfg.CharCount), text.Length);
				return cfg.Direction == CleanupDirection.Start ? text[count..] : text[..^count];
			}
			case CleanupMode.Range:
			{
				int start = Math.Clamp(Math.Min(cfg.RangeStart, cfg.RangeEnd), 0, text.Length);
				int end = Math.Clamp(Math.Max(cfg.RangeStart, cfg.RangeEnd) + 1, 0, text.Length);
				if (end <= start) return text;
				return text.Remove(start, end - start);
			}
			case CleanupMode.Cleanup:
			{
				var sb = new StringBuilder(text.Length);
				foreach (char c in text)
				{
					if (cfg.RemoveDigits && char.IsDigit(c)) continue;
					if (cfg.RemoveEnglish && (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z')) continue;
					if (cfg.RemoveChinese && c >= 0x4E00 && c <= 0x9FFF) continue;
					if (cfg.RemoveSpaces && char.IsWhiteSpace(c)) continue;
					if (cfg.RemoveSymbols && !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c) && c != '_') continue;
					sb.Append(c);
				}
				return sb.ToString();
			}
			default:
				return text;
		}
	}

	// ────────────────────────── 模板变量 ──────────────────────────

	/// <summary>解析模板中的通用变量。无效占位保持原样。</summary>
	public static string ResolveTemplate(string template, FileItem file, string currentName, int ordinal)
	{
		if (string.IsNullOrEmpty(template) || template.IndexOf('{') < 0) return template;

		var sb = new StringBuilder(template.Length);
		for (int i = 0; i < template.Length; i++)
		{
			char c = template[i];
			if (c == '{')
			{
				int end = template.IndexOf('}', i);
				if (end < 0) { sb.Append(c); continue; }
				string token = template[(i + 1)..end];
				string? replacement = ResolveToken(token, file, currentName, ordinal);
				if (replacement is not null)
				{
					sb.Append(replacement);
					i = end;
					continue;
				}
			}
			sb.Append(c);
		}
		return sb.ToString();
	}

	private static string? ResolveToken(string token, FileItem file, string currentName, int ordinal)
	{
		if (token == "n") return ordinal.ToString();
		if (token == "name") return currentName;
		if (token == "folderName") return Path.GetFileName(file.Directory);
		if (token == "relativePath") return file.FullPath;
		if (token == "date") return DateTime.Now.ToString("yyyy-MM-dd");
		if (token == "time") return DateTime.Now.ToString("HH-mm-ss");
		if (token == "datetime") return DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");
		if (token == "timestamp") return DateTimeOffset.Now.ToUnixTimeSeconds().ToString();
		if (token.StartsWith("date:", StringComparison.Ordinal))
		{
			string fmt = token["date:".Length..]
				.Replace("YYYY", "yyyy")
				.Replace("DD", "dd");
			try
			{
				return DateTime.Now.ToString(fmt);
			}
			catch (FormatException)
			{
				return "{" + token + "}"; // 非法日期格式：原样保留
			}
		}
		return null;
	}
}
