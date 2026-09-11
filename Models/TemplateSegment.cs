using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;

namespace RenameTool.Models;

/// <summary>名称模板中的片段类型。</summary>
public enum TemplateSegmentKind
{
	Text,
	Variable,
	/// <summary>自定义文本：作为独立模块存在，内部含一个可编辑的自适应文本框。</summary>
	Custom,
}

/// <summary>
/// 名称模板编辑器的一个片段：文本段可直接编辑，变量段作为整体模块（可拖拽排序 / 一键删除），
/// 自定义文本段同样是整体模块，但内部文字可直接编辑。
/// </summary>
public sealed class TemplateSegment : ObservableObject
{
	public TemplateSegmentKind Kind { get; init; }

	public bool IsPlainText => Kind == TemplateSegmentKind.Text;
	public bool IsText => Kind == TemplateSegmentKind.Text;
	public bool IsVariable => Kind == TemplateSegmentKind.Variable;
	public bool IsCustom => Kind == TemplateSegmentKind.Custom;

	private string _value = "";
	public string Value
	{
		get => _value;
		set => SetProperty(ref _value, value);
	}

	public static TemplateSegment Text(string value) => new() { Kind = TemplateSegmentKind.Text, Value = value };
	public static TemplateSegment Variable(string token) => new() { Kind = TemplateSegmentKind.Variable, Value = token };
	public static TemplateSegment Custom(string value) => new() { Kind = TemplateSegmentKind.Custom, Value = value };
}

/// <summary>名称模板可用变量（供选择面板展示与插入）。</summary>
public sealed record TemplateVariable(string Token, string Label, string Description);

/// <summary>可用变量注册表。</summary>
public static class TemplateVariables
{
	public static IReadOnlyList<TemplateVariable> All { get; } =
	[
		new("{n}", "序号", "{n}：当前文件的序号"),
		new("{name}", "原文件名", "{name}：当前文件名（不含扩展名）"),
		new("{folderName}", "所在文件夹名", "{folderName}：文件所在文件夹的名称"),
		new("{relativePath}", "完整路径", "{relativePath}：文件的完整路径（含目录）"),
		new("{date}", "日期", "{date}：当天日期，格式 yyyy-MM-dd"),
		new("{time}", "时间", "{time}：当前时间，格式 HH-mm-ss"),
		new("{datetime}", "日期时间", "{datetime}：日期与时间，格式 yyyy-MM-dd-HH-mm-ss"),
		new("{timestamp}", "时间戳", "{timestamp}：Unix 时间戳（秒）"),
		new("{date:yyyyMMdd}", "自定义日期", "{date:格式}：按自定义格式输出日期，例如 {date:yyyyMMdd}"),
	];

	private static readonly HashSet<string> Known = BuildKnown();

	private static HashSet<string> BuildKnown()
	{
		var set = new HashSet<string>(StringComparer.Ordinal);
		foreach (var v in All)
			set.Add(v.Token);
		return set;
	}

	/// <summary>判断 {token} 是否为已知变量（{date:任意格式} 也视为变量）。</summary>
	public static bool IsKnown(string token)
		=> Known.Contains("{" + token + "}") || token.StartsWith("date:", StringComparison.Ordinal);
}

/// <summary>名称模板的字符串 ⇄ 片段集合转换。</summary>
public static class TemplateCodec
{
	/// <summary>把模板字符串拆分为片段；空模板返回单个空文本段，保证编辑区始终可输入。</summary>
	public static ObservableCollection<TemplateSegment> Parse(string? template)
	{
		var list = new ObservableCollection<TemplateSegment>();
		string s = template ?? "";
		int i = 0, textStart = 0;
		while (i < s.Length)
		{
			if (s[i] == '{')
			{
				int end = s.IndexOf('}', i);
				if (end > i + 1 && TemplateVariables.IsKnown(s[(i + 1)..end]))
				{
					if (i > textStart) list.Add(TemplateSegment.Text(s[textStart..i]));
					list.Add(TemplateSegment.Variable(s[i..(end + 1)]));
					i = end + 1;
					textStart = i;
					continue;
				}
			}
			i++;
		}
		if (textStart < s.Length) list.Add(TemplateSegment.Text(s[textStart..]));
		if (list.Count == 0) list.Add(TemplateSegment.Text(""));
		return list;
	}

	/// <summary>把片段集合拼回模板字符串。</summary>
	public static string Compose(IEnumerable<TemplateSegment> segments)
	{
		var sb = new StringBuilder();
		foreach (var segment in segments) sb.Append(segment.Value);
		return sb.ToString();
	}
}

/// <summary>
/// 把“模板字符串”与“可编辑片段集合”双向绑定：文本段可直接输入，变量段作为整体模块（可拖拽排序 / 一键删除）。
/// 片段变化时自动回写字符串并请求刷新预览。名称模板、替换为、插入文本共用此编辑区。
/// </summary>
public sealed class SegmentEditor : ObservableObject
{
	private readonly Action<string> _apply;

	public ObservableCollection<TemplateSegment> Segments { get; }

	/// <summary>编辑区是否已包含模块（变量 / 自定义文本）。末尾空文本段仅在已有模块时隐藏，
	/// 既避免末尾留下空白，又保证纯文本模板仍有可输入的落点。</summary>
	public bool HasModule
	{
		get
		{
			foreach (var segment in Segments)
				if (!segment.IsText) return true;
			return false;
		}
	}

	public SegmentEditor(string? initial, Action<string> apply)
	{
		_apply = apply;
		Segments = TemplateCodec.Parse(initial);
		Segments.CollectionChanged += OnCollectionChanged;
		foreach (var segment in Segments) segment.PropertyChanged += OnItemChanged;
	}

	/// <summary>把变量以模块形式插入末尾；末尾始终保留一个空文本段，便于在变量后继续输入。</summary>
	public void InsertVariable(string token)
	{
		if (Segments.Count == 0 || !(Segments[^1].IsText && Segments[^1].Value.Length == 0))
			Segments.Add(TemplateSegment.Text(""));
		Segments.Insert(Segments.Count - 1, TemplateSegment.Variable(token));
	}

	/// <summary>在末尾插入一个“自定义文本”模块（内含可编辑的自适应文本框）。</summary>
	public void InsertCustomText()
	{
		if (Segments.Count == 0 || !(Segments[^1].IsText && Segments[^1].Value.Length == 0))
			Segments.Add(TemplateSegment.Text(""));
		Segments.Insert(Segments.Count - 1, TemplateSegment.Custom(""));
	}

	/// <summary>删除一个模块/文本段；若删空则补一个空文本段，保持编辑区可输入。</summary>
	public void Remove(TemplateSegment segment)
	{
		Segments.Remove(segment);
		if (Segments.Count == 0) Segments.Add(TemplateSegment.Text(""));
	}

	/// <summary>
	/// 把已有的非空普通文本段整体转为“自定义文本”模块。
	/// 变量模式下内联文本段只读且没有删除入口，转为可编辑、可删除的自定义文本模块。
	/// </summary>
	public void PromoteTextToCustom()
	{
		for (int i = 0; i < Segments.Count; i++)
		{
			var segment = Segments[i];
			if (segment.Kind == TemplateSegmentKind.Text && segment.Value.Length > 0)
				Segments[i] = TemplateSegment.Custom(segment.Value);
		}
	}

	/// <summary>拖拽排序：把 <paramref name="segment"/> 移到 <paramref name="before"/> 之前（before 为 null 表示移到最后）。</summary>
	public void Move(TemplateSegment segment, TemplateSegment? before)
	{
		int from = Segments.IndexOf(segment);
		if (from < 0) return;

		int to;
		if (before is null)
		{
			to = Segments.Count - 1;             // 空白处：移到最后
		}
		else
		{
			to = Segments.IndexOf(before);
			if (to < 0) to = Segments.Count - 1;
			else if (from < to) to--;            // 后移时，移除自身会让目标索引前移一位
		}
		if (from != to) Segments.Move(from, to);
	}

	/// <summary>把片段移动到指定索引（拖拽排序实时重排用，索引自动收敛到合法范围）。</summary>
	public void MoveTo(TemplateSegment segment, int index)
	{
		int from = Segments.IndexOf(segment);
		if (from < 0) return;
		int to = Math.Clamp(index, 0, Segments.Count - 1);
		if (from != to) Segments.Move(from, to);
	}

	private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.OldItems is not null)
			foreach (TemplateSegment segment in e.OldItems) segment.PropertyChanged -= OnItemChanged;
		if (e.NewItems is not null)
			foreach (TemplateSegment segment in e.NewItems) segment.PropertyChanged += OnItemChanged;
		Sync();
	}

	private void OnItemChanged(object? sender, PropertyChangedEventArgs e) => Sync();

	/// <summary>片段 → 字符串，并请求刷新预览。</summary>
	private void Sync()
	{
		RaisePropertyChanged(nameof(HasModule));
		_apply(TemplateCodec.Compose(Segments));
	}
}
