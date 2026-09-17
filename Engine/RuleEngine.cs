using System.Collections.Concurrent;
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
	// ────────────────────────── 正则缓存 ──────────────────────────

	/// <summary>正则实例缓存：预览每敲一个键都会整批重算，而 <see cref="Regex"/> 的编译成本远高于匹配本身，
	/// 逐文件 <c>new Regex</c> 在大批量下会明显拖慢按键响应。
	/// 键 = 表达式 + 是否区分大小写（两者共同决定编译结果与超时设置）。</summary>
	private static readonly ConcurrentDictionary<(string Pattern, bool CaseSensitive), Regex> RegexCache = new();

	/// <summary>缓存上限。规则数量本就很少，超出即整体清空，无需 LRU。</summary>
	private const int RegexCacheLimit = 64;

	/// <summary>取（或编译并缓存）正则实例；表达式非法时抛 <see cref="ArgumentException"/>，与直接 new 的语义一致。</summary>
	private static Regex GetRegex(string pattern, bool caseSensitive)
	{
		var key = (pattern, caseSensitive);
		if (RegexCache.TryGetValue(key, out var cached)) return cached;

		var options = RegexOptions.None;
		if (!caseSensitive) options |= RegexOptions.IgnoreCase;
		var rx = new Regex(pattern, options, RuleConfig.RegexTimeout);

		if (RegexCache.Count >= RegexCacheLimit) RegexCache.Clear();
		RegexCache[key] = rx;
		return rx;
	}

	// ────────────────────────── 主链 ──────────────────────────

	/// <summary>对单个文件应用整条启用的规则链，返回最终名称。</summary>
	/// <param name="file">文件</param>
	/// <param name="ordinal">该文件在激活列表中的序号（从 1 起，供 {n} 变量）</param>
	/// <param name="sequenceMaps">序号规则预计算结果：ruleId → 文件 → 序号文本</param>
	/// <param name="swapMaps">成对交换规则预计算结果：ruleId → 文件 → 该文件要拿走的伙伴名称</param>
	public static string ApplyChain(FileItem file, int ordinal, IReadOnlyList<RenameRule> rules,
		IReadOnlyDictionary<string, Dictionary<FileItem, string>> sequenceMaps,
		IReadOnlyDictionary<string, Dictionary<FileItem, string>> swapMaps)
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
			else if (rule.Type == RuleType.PairSwap)
			{
				// 交换不是逐文件可算的：伙伴名由 PreviewEngine 按「本条规则之前」的规则链预先算出。
				// 无伙伴（奇数个文件落单、或未参与）时保持原名。
				var map = swapMaps.TryGetValue(rule.Id, out var s) ? s : null;
				if (map is not null && map.TryGetValue(file, out var partnerName))
					current = SwapByScope(rule.Scope, current, partnerName);
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

	/// <summary>成对交换：按作用域取出伙伴名称中对应的部分，与本名称的另一部分重组。</summary>
	private static string SwapByScope(ExtensionScope scope, string name, string partnerName)
	{
		var (myBase, myExt) = NameUtils.Split(name);
		var (partnerBase, partnerExt) = NameUtils.Split(partnerName);
		return scope switch
		{
			ExtensionScope.Name => NameUtils.Join(partnerBase, myExt),
			ExtensionScope.Extension => NameUtils.Join(myBase, partnerExt),
			_ => partnerName,
		};
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
				var rx = GetRegex(cfg.Find, cfg.CaseSensitive);
				return cfg.MatchAll
					? rx.Replace(text, replacement)
					: rx.Replace(text, replacement, 1);
			}
			catch (RegexMatchTimeoutException)
			{
				// 灾难性回溯超时：本条按未命中处理，避免整个预览 / 执行被拖死；
				// 同时把原因写到规则面板上——静默改写结果比卡住更难以察觉。
				cfg.ReportRegexTimeout();
				return text;
			}
			catch (ArgumentException)
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
		if (token == "ext") return file.Extension.TrimStart('.');
		if (token == "size") return file.Size.FormatFileSize();
		if (token == "folderName") return Path.GetFileName(file.Directory);
		if (token == "parent") return Path.GetFileName(Path.GetDirectoryName(file.Directory) ?? "");
		if (token == "date") return DateTime.Now.ToString("yyyy-MM-dd");
		if (token == "time") return DateTime.Now.ToString("HH-mm-ss");
		if (token == "datetime") return DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");
		if (token == "timestamp") return DateTimeOffset.Now.ToUnixTimeSeconds().ToString();
		if (token == "modified") return file.Modified.ToString("yyyy-MM-dd");
		if (token == "created") return file.Created.ToString("yyyy-MM-dd");
		return null;
	}
}
