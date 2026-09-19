using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RenameTool.Services;

/// <summary>一次批次中的单条改名操作（仅保存路径信息，便于持久化与回滚）。</summary>
public sealed class HistoryOp
{
	public string Directory { get; set; } = "";
	public string OldName { get; set; } = "";
	public string NewName { get; set; } = "";

	[JsonIgnore] public string OldPath => Path.Combine(Directory, OldName);
	[JsonIgnore] public string NewPath => Path.Combine(Directory, NewName);
}

/// <summary>一次重命名批次的完整记录，支持整体回滚。</summary>
public sealed class HistoryEntry : ObservableObject
{
	public string Id { get; set; } = Guid.NewGuid().ToString("N");
	public DateTime Time { get; set; } = DateTime.Now;
	public string RuleSummary { get; set; } = "";
	public List<HistoryOp> Ops { get; set; } = [];

	private bool _rolledBack;
	public bool RolledBack
	{
		get => _rolledBack;
		set
		{
			if (!SetProperty(ref _rolledBack, value)) return;
			// 状态文本由 RolledBack 派生，需一并通知，历史列表才能立即刷新（第8项）
			RaisePropertyChanged(nameof(StatusText));
		}
	}

	[JsonIgnore] public string TimeText => Time.ToString("MM-dd HH:mm:ss");
	[JsonIgnore] public string CountText => $"{Ops.Count} 个文件";
	[JsonIgnore] public string StatusText => RolledBack ? "已回滚" : "已执行";
	[JsonIgnore] public string DetailText => string.IsNullOrWhiteSpace(RuleSummary)
		? CountText
		: $"{CountText} · {RuleSummary}";
}

/// <summary>批次历史的读写与整体回滚（history.json）。</summary>
public static class HistoryStore
{
	private const int MaxEntries = 100;

	private static readonly JsonSerializerOptions Options = new()
	{
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter() },
	};

	public static List<HistoryEntry> Load()
	{
		string? json = AppStorage.TryRead(AppStorage.HistoryFile);
		if (string.IsNullOrWhiteSpace(json)) return [];
		try { return JsonSerializer.Deserialize<List<HistoryEntry>>(json, Options) ?? []; }
		catch (Exception ex)
		{
			// 内容已损坏：先留一份副本再当作空历史，避免随后的保存把原文件覆盖掉
			AppStorage.BackupCorrupt(AppStorage.HistoryFile, ex);
			return [];
		}
	}

	/// <summary>写入历史文件；超过上限时从最旧一端裁掉。
	/// 裁剪必须直接作用于调用方传入的集合本体——若只作用于副本，内存中的历史会无上限增长，
	/// 而磁盘文件却始终只有 100 条，两边不一致。</summary>
	public static void Save(IList<HistoryEntry> entries)
	{
		while (entries.Count > MaxEntries) entries.RemoveAt(0);
		AppStorage.Write(AppStorage.HistoryFile, JsonSerializer.Serialize(entries, Options));
	}

	/// <summary>回滚一条历史批次：按逆序把文件从新名改回原名。返回失败条数。</summary>
	public static int Rollback(HistoryEntry entry, out string? firstError)
	{
		firstError = null;
		int failed = 0;
		for (int i = entry.Ops.Count - 1; i >= 0; i--)
		{
			HistoryOp op = entry.Ops[i];
			try
			{
				string source = op.NewPath;
				string target = op.OldPath;
				if (!File.Exists(source)) { failed++; firstError ??= "文件已不存在，无法回滚。"; continue; }
				if (File.Exists(target)) { failed++; firstError ??= $"原名已被占用：{op.OldName}"; continue; }
				File.Move(source, target);
			}
			catch (Exception ex)
			{
				failed++;
				firstError ??= ex.Message;
			}
		}
		if (failed == 0) entry.RolledBack = true;
		return failed;
	}
}
