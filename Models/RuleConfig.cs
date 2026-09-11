using System.Text.Json.Serialization;

namespace RenameTool.Models;

/// <summary>单条规则的参数（判别联合式：Type 决定哪些字段有意义）。</summary>
public sealed class RuleConfig : ObservableObject
{
	public RuleType Type { get; set; }

	// ── findReplace ──
	private string _find = "";
	public string Find
	{
		get => _find;
		set => SetProperty(ref _find, value);
	}

	private string _replace = "";
	public string Replace
	{
		get => _replace;
		set => SetProperty(ref _replace, value);
	}

	private bool _caseSensitive;
	public bool CaseSensitive
	{
		get => _caseSensitive;
		set => SetProperty(ref _caseSensitive, value);
	}

	private bool _matchAll = true;     // false = 仅替换第一处
	public bool MatchAll
	{
		get => _matchAll;
		set => SetProperty(ref _matchAll, value);
	}

	private bool _useRegex;
	public bool UseRegex
	{
		get => _useRegex;
		set
		{
			if (SetProperty(ref _useRegex, value)) RaisePropertyChanged(nameof(AllowInlineInput));
		}
	}

	// ── insert ──
	private string _text = "";
	public string Text
	{
		get => _text;
		set => SetProperty(ref _text, value);
	}

	private bool _useVariables;
	/// <summary>插入文本规则：是否显示变量区域（勾选后可在文本中插入 {变量}）。</summary>
	public bool UseVariables
	{
		get => _useVariables;
		set
		{
			if (SetProperty(ref _useVariables, value)) RaisePropertyChanged(nameof(AllowInlineInput));
		}
	}

	/// <summary>
	/// 变量模式（正则 / 变量）开启后，编辑区内联文本段不再接受输入，
	/// 需要自定义文字时改用“自定义文本”模块。
	/// </summary>
	[JsonIgnore]
	public bool AllowInlineInput => !(UseRegex || UseVariables);

	private InsertPosition _insertAt = InsertPosition.Start;
	public InsertPosition InsertAt
	{
		get => _insertAt;
		set => SetProperty(ref _insertAt, value);
	}

	private int _insertIndex;
	public int InsertIndex
	{
		get => _insertIndex;
		set => SetProperty(ref _insertIndex, value);
	}

	// ── sequence ──
	private SeqType _seqType = SeqType.Numeric;
	public SeqType SeqType
	{
		get => _seqType;
		set => SetProperty(ref _seqType, value);
	}

	private long _start = 1;
	public long Start
	{
		get => _start;
		set => SetProperty(ref _start, value);
	}

	private long _step = 1;
	public long Step
	{
		get => _step;
		set => SetProperty(ref _step, value);
	}

	private int _padding = 1;
	public int Padding
	{
		get => _padding;
		set => SetProperty(ref _padding, value);
	}

	private SeqPosition _seqPosition = SeqPosition.Start;
	public SeqPosition SeqPosition
	{
		get => _seqPosition;
		set => SetProperty(ref _seqPosition, value);
	}

	/// <summary>前/后缀模式中序号与原名之间的分隔文本；留空则紧贴拼接。</summary>
	private string _separator = "_";
	public string Separator
	{
		get => _separator;
		set => SetProperty(ref _separator, value);
	}

	/// <summary>名称模板规则使用：整段名称由模板构造（{n} = 序号，{name} = 原名）。</summary>
	private string _template = "";
	public string Template
	{
		get => _template;
		set => SetProperty(ref _template, value);
	}

	private SeqScope _scope = SeqScope.Global;
	public SeqScope Scope
	{
		get => _scope;
		set => SetProperty(ref _scope, value);
	}

	private bool _sortBeforeNumbering;
	public bool SortBeforeNumbering
	{
		get => _sortBeforeNumbering;
		set => SetProperty(ref _sortBeforeNumbering, value);
	}

	private SeqSortBy _sortBy = SeqSortBy.ListOrder;
	public SeqSortBy SortBy
	{
		get => _sortBy;
		set => SetProperty(ref _sortBy, value);
	}

	private bool _sortAscending = true;
	public bool SortAscending
	{
		get => _sortAscending;
		set => SetProperty(ref _sortAscending, value);
	}

	private bool _naturalSort = true;
	public bool NaturalSort
	{
		get => _naturalSort;
		set => SetProperty(ref _naturalSort, value);
	}

	// ── case ──
	private CaseMode _caseMode = CaseMode.None;
	public CaseMode CaseMode
	{
		get => _caseMode;
		set => SetProperty(ref _caseMode, value);
	}

	private WordStyle _wordStyle = WordStyle.None;
	public WordStyle WordStyle
	{
		get => _wordStyle;
		set => SetProperty(ref _wordStyle, value);
	}

	// ── removeCleanup ──
	private CleanupMode _cleanupMode = CleanupMode.Cleanup;
	public CleanupMode CleanupMode
	{
		get => _cleanupMode;
		set => SetProperty(ref _cleanupMode, value);
	}

	private CleanupDirection _direction = CleanupDirection.Start;
	public CleanupDirection Direction
	{
		get => _direction;
		set => SetProperty(ref _direction, value);
	}

	private int _charCount = 1;
	public int CharCount
	{
		get => _charCount;
		set => SetProperty(ref _charCount, value);
	}

	private int _rangeStart;
	public int RangeStart
	{
		get => _rangeStart;
		set => SetProperty(ref _rangeStart, value);
	}

	private int _rangeEnd = 1;
	public int RangeEnd
	{
		get => _rangeEnd;
		set => SetProperty(ref _rangeEnd, value);
	}

	private bool _removeDigits;
	public bool RemoveDigits
	{
		get => _removeDigits;
		set => SetProperty(ref _removeDigits, value);
	}

	private bool _removeEnglish;
	public bool RemoveEnglish
	{
		get => _removeEnglish;
		set => SetProperty(ref _removeEnglish, value);
	}

	private bool _removeChinese;
	public bool RemoveChinese
	{
		get => _removeChinese;
		set => SetProperty(ref _removeChinese, value);
	}

	private bool _removeSpaces;
	public bool RemoveSpaces
	{
		get => _removeSpaces;
		set => SetProperty(ref _removeSpaces, value);
	}

	private bool _removeSymbols;
	public bool RemoveSymbols
	{
		get => _removeSymbols;
		set => SetProperty(ref _removeSymbols, value);
	}

	public static RuleConfig CreateDefault(RuleType type) => new() { Type = type };
}

/// <summary>规则链中的一条：启用状态 + 参数 + 应用范围。</summary>
public sealed class RenameRule : ObservableObject
{
	public string Id { get; } = Guid.NewGuid().ToString("N");
	public required RuleType Type { get; init; }
	public required RuleConfig Config { get; init; }

	private bool _enabled = true;
	public bool Enabled
	{
		get => _enabled;
		set => SetProperty(ref _enabled, value);
	}

	private ExtensionScope _scope = ExtensionScope.Name;
	public ExtensionScope Scope
	{
		get => _scope;
		set => SetProperty(ref _scope, value);
	}

	/// <summary>规则类型中文名（供 UI 展示）。</summary>
	[JsonIgnore]
	public string TypeLabel => RuleTypeNames.Label(Type);

	// ─────────────── 模块化编辑区（名称模板 / 替换为 / 插入文本） ───────────────

	private SegmentEditor? _templateEditor;
	/// <summary>名称模板编辑区（对应 <see cref="RuleConfig.Template"/>）。</summary>
	public SegmentEditor TemplateEditor
		=> _templateEditor ??= new SegmentEditor(Config.Template, v => Config.Template = v);

	private SegmentEditor? _replaceEditor;
	/// <summary>查找替换“替换为”编辑区（对应 <see cref="RuleConfig.Replace"/>）。</summary>
	public SegmentEditor ReplaceEditor
		=> _replaceEditor ??= new SegmentEditor(Config.Replace, v => Config.Replace = v);

	private SegmentEditor? _insertEditor;
	/// <summary>插入文本编辑区（对应 <see cref="RuleConfig.Text"/>）。</summary>
	public SegmentEditor InsertEditor
		=> _insertEditor ??= new SegmentEditor(Config.Text, v => Config.Text = v);

	/// <summary>
	/// 变量模式切换后重新解析对应编辑区：使普通输入框与变量块编辑区内容保持一致；
	/// 并把已有文本转为“自定义文本”模块（变量模式下内联文本段不可编辑）。
	/// </summary>
	public void ReloadEditors()
	{
		switch (Type)
		{
			case RuleType.FindReplace:
				_replaceEditor = null;
				ReplaceEditor.PromoteTextToCustom();
				RaisePropertyChanged(nameof(ReplaceEditor));
				break;
			case RuleType.Insert:
				_insertEditor = null;
				InsertEditor.PromoteTextToCustom();
				RaisePropertyChanged(nameof(InsertEditor));
				break;
		}
	}
}
