using System.IO;

namespace RenameTool.Services;

/// <summary>应用本地数据目录（%LocalAppData%\RenameTool）与各类持久化文件路径。</summary>
public static class AppStorage
{
	public static string Root { get; } = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RenameTool");

	public static string SettingsFile => Path.Combine(Root, "settings.json");
	public static string PresetsFile => Path.Combine(Root, "presets.json");
	public static string HistoryFile => Path.Combine(Root, "history.json");
	public static string ErrorLogFile => Path.Combine(Root, "error.log");

	/// <summary>确保数据目录存在（写入前调用）。</summary>
	public static void EnsureRoot() => Directory.CreateDirectory(Root);

	/// <summary>安全读取文本文件；不存在或读取失败返回 null。</summary>
	public static string? TryRead(string path)
	{
		try { return File.Exists(path) ? File.ReadAllText(path) : null; }
		catch { return null; }
	}

	/// <summary>安全写入文本文件（自动创建目录）。
	/// 采用「先写临时文件、再整体替换」的方式：直接覆盖写入一旦中途失败（磁盘满、断电、进程被杀），
	/// 原文件会被截断成半截内容，导致设置或历史整体丢失且无法恢复。
	/// 替换用 <see cref="File.Replace(string, string, string?)"/>（同卷原子替换），正式文件任何时刻都是完整版本。
	/// 写入失败不向外抛（调用方基本都是收尾保存），但会把原因追加进 error.log，失败不再无声无息。</summary>
	public static void Write(string path, string content)
	{
		string temp = path + ".tmp";
		try
		{
			EnsureRoot();
			File.WriteAllText(temp, content);
			// 目标不存在时 Replace 会抛 FileNotFoundException，这种一次性场景退化为覆盖式 Move
			if (File.Exists(path)) File.Replace(temp, path, destinationBackupFileName: null);
			else File.Move(temp, path, overwrite: true);
		}
		catch (Exception ex)
		{
			// 忽略写入失败，但不要把半成品临时文件留在目录里
			try { if (File.Exists(temp)) File.Delete(temp); } catch { /* 忽略 */ }
			// 若失败的正是日志本身，就不能再往日志里写（会递归，且很可能同样失败）
			if (!string.Equals(path, ErrorLogFile, StringComparison.OrdinalIgnoreCase))
				TryAppendErrorLog($"写入失败：{path}\n{ex}");
		}
	}

	/// <summary>把一条记录追加进 error.log；任何失败都吞掉（日志写不进去也不能影响主流程）。</summary>
	public static void TryAppendErrorLog(string message)
	{
		try
		{
			EnsureRoot();
			File.AppendAllText(ErrorLogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n\n");
		}
		catch { /* 日志写不进去只能放弃 */ }
	}
}
