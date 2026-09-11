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
