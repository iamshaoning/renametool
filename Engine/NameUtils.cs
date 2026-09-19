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
		// GetInvalidFileNameChars() 在 Windows 上已包含 0x00–0x1F 与 " < > | : * ? \ /，
		// 无需再单独扫一遍控制字符。
		if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return true;
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
			IsReservedNumber(stem[3]))
			return true;
		return false;
	}

	/// <summary>
	/// 设备名后缀数字：Windows 保留的是 COM1..COM9 / LPT1..LPT9，<c>COM0</c>/<c>LPT0</c> 并不保留；
	/// 同时系统按「数字」对待上标 ¹²³（U+00B9/B2/B3），因此 COM¹ 同样被拦截。
	/// <c>char.IsDigit</c> 对这两点都不成立（'0' 返回 true、'¹' 返回 false），故不能直接用它。
	/// </summary>
	private static bool IsReservedNumber(char c)
		=> c is >= '1' and <= '9' or '\u00B9' or '\u00B2' or '\u00B3';

	/// <summary>Windows 下重名比较（大小写不敏感）。</summary>
	public static bool SameName(string a, string b)
		=> string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

	/// <summary>名称完全一致（区分大小写）：用于判断是否真的需要改名（含仅大小写变化）。</summary>
	public static bool ExactName(string a, string b)
		=> string.Equals(a, b, StringComparison.Ordinal);

	/// <summary>
	/// 最大完整路径长度（含目录与文件名）：超过此长度在部分环境（网络盘 / 旧接口）下无法改名。
	/// app.manifest 虽已声明 <c>longPathAware</c>，但该声明只有在系统开关
	/// <c>HKLM\SYSTEM\CurrentControlSet\Control\FileSystem\LongPathsEnabled</c> 打开后才真正生效；
	/// 开关关闭时超过 260 的路径依旧失败。故此处按开关的实际状态取上限，
	/// 避免出现「清单声称支持长路径、程序自己却先拦下」的自相矛盾。
	/// </summary>
	public static int MaxFullPathLength { get; } = DetectMaxFullPathLength();

	/// <summary>未开启长路径时的传统上限。</summary>
	private const int LegacyMaxPathLength = 260;

	/// <summary>开启长路径后的上限：Windows 为 32767，留出余量。</summary>
	private const int ExtendedMaxPathLength = 32000;

	private static int DetectMaxFullPathLength()
	{
		try
		{
			using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
				@"SYSTEM\CurrentControlSet\Control\FileSystem");
			if (key?.GetValue("LongPathsEnabled") is int enabled && enabled == 1)
				return ExtendedMaxPathLength;
		}
		catch (Exception)
		{
			// 读不到（策略限制 / 权限受限）时按保守值处理：宁可提前提示，也不要改名失败。
		}
		return LegacyMaxPathLength;
	}

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
