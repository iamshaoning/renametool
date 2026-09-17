using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RenameTool.Models;

namespace RenameTool.Services;

/// <summary>
/// 规则预设的持久化结构：与运行时 <see cref="RenameRule"/> 解耦，
/// 只保留规则参数，避免把编辑区等运行时状态写盘。
/// </summary>
public sealed class PresetRule
{
	public RuleType Type { get; set; }
	public bool Enabled { get; set; } = true;
	public ExtensionScope Scope { get; set; } = ExtensionScope.Name;
	public RuleConfig Config { get; set; } = new();
}

/// <summary>一组具名规则预设。</summary>
public sealed class RulePreset
{
	public string Name { get; set; } = "";
	public List<PresetRule> Rules { get; set; } = [];

	[JsonIgnore] public string CountText => $"{Rules.Count} 条规则";
	[JsonIgnore] public string SummaryText => Rules.Count == 0
		? "（空预设）"
		: string.Join(" + ", Rules.Where(r => r.Enabled).Select(r => RuleTypeNames.Label(r.Type)));
}

/// <summary>规则预设的读写（presets.json）。</summary>
public static class PresetStore
{
	private static readonly JsonSerializerOptions Options = new()
	{
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter() },
	};

	public static PresetRule ToData(RenameRule rule) => new()
	{
		Type = rule.Type,
		Enabled = rule.Enabled,
		Scope = rule.Scope,
		Config = rule.Config,
	};

	public static RenameRule FromData(PresetRule data) => new()
	{
		Type = data.Type,
		Config = data.Config ?? RuleConfig.CreateDefault(data.Type),
		Enabled = data.Enabled,
		Scope = data.Scope,
	};

	public static List<PresetRule> Snapshot(IEnumerable<RenameRule> rules) => [.. rules.Select(ToData)];
	public static List<RenameRule> Materialize(IEnumerable<PresetRule> rules) => [.. rules.Select(FromData)];

	public static List<RulePreset> LoadPresets()
	{
		string? json = AppStorage.TryRead(AppStorage.PresetsFile);
		if (string.IsNullOrWhiteSpace(json)) return [];
		try { return JsonSerializer.Deserialize<List<RulePreset>>(json, Options) ?? []; }
		catch { return []; }
	}

	public static void SavePresets(List<RulePreset> presets)
		=> AppStorage.Write(AppStorage.PresetsFile, JsonSerializer.Serialize(presets, Options));

	/// <summary>把预设集合导出为 JSON 文件（备份或迁移到其它机器）。写入失败会抛出异常，由调用方提示。</summary>
	public static void ExportToFile(string path, List<RulePreset> presets)
		=> File.WriteAllText(path, JsonSerializer.Serialize(presets, Options));

	/// <summary>从 JSON 文件读取预设集合；文件损坏会抛出异常，由调用方提示。</summary>
	public static List<RulePreset> ImportFromFile(string path)
	{
		string json = File.ReadAllText(path);
		if (string.IsNullOrWhiteSpace(json)) return [];
		return JsonSerializer.Deserialize<List<RulePreset>>(json, Options) ?? [];
	}
}
