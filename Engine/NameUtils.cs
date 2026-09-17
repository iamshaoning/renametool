using System.IO;
using RenameTool.Models;

namespace RenameTool.Engine;

public static class NameUtils
{
	/// <summary>拆分为 基名 + 扩展名。以最后一个点分割；无点则扩展名为空。</summary>
	public static (string BaseName, string Extension) Split(string name)
	{
		int dot = name.LastIndexOf('.');
		if (dot <= 0) return (name, "");
		return (name[..dot], name[dot..]);
	}

	/// <summary>合并基名与扩展名。</summary>
	public static string Join(string baseName, string extension) => baseName + extension;

	/// <summary>Windows 文件名非法字符（含结尾的空格/点——系统会静默去除，导致实际名称与预期不符）。</summary>
	public static bool HasIllegalChars(string name)
	{
		if (name.Length == 0) return false;
		if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return true;
		foreach (char c in name)
			if (c <= 31) return true;
		return name[^1] is ' ' or '.';
	}

	/// <summary>文件名去除 Windows 保留名称（CON、PRN、AUX、NUL、COM1..9、LPT1..9）的后缀点问题。</summary>
	public static bool IsReservedName(string name)
	{
		string stem = name.Split('.')[0].Trim();
		if (stem.Length == 0) return false;
		if (stem.Length == 3 && stem.Equals("CON", StringComparison.OrdinalIgnoreCase)) return true;
		if (stem.Length == 3 && stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)) return true;
		if (stem.Length == 3 && stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)) return true;
		if (stem.Length == 3 && stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)) return true;
		if (stem.Length == 4 &&
			(stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
			 stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
			char.IsDigit(stem[3]))
			return true;
		return false;
	}

	/// <summary>Windows 下重名比较（大小写不敏感）。</summary>
	public static bool SameName(string a, string b)
		=> string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

	/// <summary>名称完全一致（区分大小写）：用于判断是否真的需要改名（含仅大小写变化）。</summary>
	public static bool ExactName(string a, string b)
		=> string.Equals(a, b, StringComparison.Ordinal);

	/// <summary>最大完整路径长度（含目录与文件名）：超过此长度在部分环境（网络盘/旧接口）下无法改名。</summary>
	public const int MaxFullPathLength = 260;

	/// <summary>
	/// 生成不冲突的名称：原名可用则返回原名，否则在基名后追加 “ (1)”“ (2)”…（扩展名保留在末尾）。
	/// </summary>
	public static string MakeUnique(string name, Func<string, bool> isTaken)
	{
		if (!isTaken(name)) return name;
		(var baseName, var extension) = Split(name);
		for (int i = 1; i < int.MaxValue; i++)
		{
			string candidate = $"{baseName} ({i}){extension}";
			if (!isTaken(candidate)) return candidate;
		}
		return name;
	}
}
