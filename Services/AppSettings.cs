using System.Text.Json;
using System.Text.Json.Serialization;

namespace RenameTool.Services;

/// <summary>用户偏好与窗口状态：尺寸 / 位置 / 最大化 / 三栏拖拽比例 / 主题模式。</summary>
public sealed class AppSettings
{
	public double Width { get; set; } = 1360;
	public double Height { get; set; } = 860;
	public double? Left { get; set; }
	public double? Top { get; set; }
	public bool Maximized { get; set; }

	/// <summary>主界面三栏（文件列表 / 规则 / 预览）的拖拽宽度比例，用于还原用户调整后的分栏。</summary>
	public double? LayoutLeft { get; set; }
	public double? LayoutMid { get; set; }
	public double? LayoutRight { get; set; }

	/// <summary>主题模式：system（跟随系统）/ light（日间）/ dark（夜间）。</summary>
	public string Theme { get; set; } = "system";

	[JsonIgnore]
	public bool HasPosition => Left.HasValue && Top.HasValue;

	[JsonIgnore]
	public bool HasLayout => LayoutLeft is > 0 && LayoutMid is > 0 && LayoutRight is > 0;
}

/// <summary>设置的加载与保存（settings.json）。</summary>
public static class SettingsStore
{
	private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

	public static AppSettings Current { get; private set; } = new();

	public static void Load()
	{
		string? json = AppStorage.TryRead(AppStorage.SettingsFile);
		try
		{
			Current = string.IsNullOrWhiteSpace(json)
				? new()
				: JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new();
		}
		catch
		{
			Current = new();
		}
	}

	public static void Save()
		=> AppStorage.Write(AppStorage.SettingsFile, JsonSerializer.Serialize(Current, Options));
}
