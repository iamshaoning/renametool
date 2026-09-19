using System.IO;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

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
		set
		{
			if (SetProperty(ref _find, value)) ValidateRegex();
		}
	}

	private string _replace = "";
	public string Replace
	{
		get => _replace;
		set
		{
			if (SetProperty(ref _replace, value)) RaisePropertyChanged(nameof(InputWarning));
		}
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
			if (SetProperty(ref _useRegex, value))
			{
				RaisePropertyChanged(nameof(AllowInlineInput));
				ValidateRegex();
			}
		}
	}

	// ── pairSwap ──
	private PairSwapMode _swapMode = PairSwapMode.Adjacent;
	/// <summary>「成对交换」的配对方式。</summary>
	public PairSwapMode SwapMode
	{
		get => _swapMode;
		set => SetProperty(ref _swapMode, value);
	}

	// ── 正则实时校验（A5） ──

	private string _regexError = "";
	/// <summary>“查找内容”作为正则时的编译错误信息；为空表示语法合法或未启用变量模式。</summary>
	[JsonIgnore]
	public string RegexError
	{
		get => _regexError;
		private set => SetProperty(ref _regexError, value);
	}

	private string _regexGroups = "";
	/// <summary>捕获组提示：列出表达式提供的 $n 反向引用；为空表示未启用变量模式。</summary>
	[JsonIgnore]
	public string RegexGroups
	{
		get => _regexGroups;
		private set => SetProperty(ref _regexGroups, value);
	}

	/// <summary>正则匹配超时上限。文件名最长不过几百字符，正常表达式远低于此；
	/// 但形如 (a+)+$ 的嵌套量词会造成指数级回溯，没有上限时预览界面会直接卡死。</summary>
	public static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

	/// <summary>规则执行期发现表达式匹配超时（灾难性回溯）时调用：复用同一个提示位，
	/// 让「本条按未命中处理」这件事在规则面板上看得见，而不是只在结果里悄悄变样。
	/// 用户再次编辑表达式会重跑 <see cref="ValidateRegex"/> 把它清掉。</summary>
	internal void ReportRegexTimeout() =>
		RegexError = $"正则匹配超时（回溯过多，单条上限 {RegexTimeout.TotalMilliseconds:0} 毫秒），本条已按“不匹配”处理。请简化表达式，例如避免 (a+)+ 这类嵌套量词。";

	/// <summary>试编译“查找内容”并统计捕获组，供 UI 实时提示（与 RuleEngine 的编译选项保持一致）。</summary>
	private void ValidateRegex()
	{
		if (!UseRegex || string.IsNullOrEmpty(_find))
		{
			RegexError = "";
			RegexGroups = "";
			return;
		}

		try
		{
			var options = RegexOptions.None;
			if (!CaseSensitive) options |= RegexOptions.IgnoreCase;
			var regex = new Regex(_find, options, RegexTimeout);
			RegexError = "";

			// 数字组（$1 $2…）与具名组（${name}）：命名组在 .NET 中同时具有编号与名称，两者都列出
			var refs = new List<string>();
			foreach (string name in regex.GetGroupNames())
			{
				if (int.TryParse(name, out int number))
				{
					if (number != 0) refs.Add("$" + number);
				}
				else
				{
					refs.Add("${" + name + "}");
				}
			}
			RegexGroups = refs.Count == 0
				? "该表达式没有捕获组，无法使用 $1 等反向引用"
				: "捕获组：" + string.Join("  ", refs);
		}
		catch (ArgumentException ex)
		{
			RegexError = "正则表达式无效：" + ex.Message;
			RegexGroups = "";
		}
	}

	// ── insert ──
	private string _text = "";
	public string Text
	{
		get => _text;
		set
		{
			if (SetProperty(ref _text, value)) RaisePropertyChanged(nameof(InputWarning));
		}
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
		set
		{
			if (SetProperty(ref _step, value)) RaisePropertyChanged(nameof(InputWarning));
		}
	}

	// ── 编辑期即时提示（F4） ──

	/// <summary>
	/// 规则参数里「注定要产出非法文件名」的输入，在编辑这条规则时就说出来，
	/// 而不是等到底部预览行汇总——那时用户只知道某行有问题，不知道该改哪个输入框。
	/// 覆盖两类：序号步长为 0、明文里的 Windows 非法字符。
	/// 正则语法错误另有专门提示位，紧贴“查找内容”输入框显示。
	/// </summary>
	[JsonIgnore]
	public string InputWarning
	{
		get
		{
			// 步长为 0 时整组文件会落到同一个序号上（彼此全部重名）：序号计算本身按 EffectiveStep 兜底为 1，
			// 但界面上只会显示一片「冲突」，用户查不出根因，所以在这里把原因说出来。
			if (Type == RuleType.Sequence && _step == 0)
				return "步长为 0：所有文件会得到同一个序号（彼此重名）。已按步长 1 处理，请改为非 0 值。";

			foreach ((string label, string text) in LiteralInputs())
			{
				if (FirstIllegalChar(text) is not { } bad) continue;
				return $"{label}含文件名不允许的字符“{DescribeChar(bad)}”。"
					+ "Windows 文件名不能包含 \\ / : * ? \" < > | 与控制字符，请删掉或用其他字符代替。";
			}

			return "";
		}
	}

	/// <summary>
	/// 逐项列出「会原样进入新名称」的明文输入。
	/// 正则模式下的“查找内容”刻意不列：其中的 ? * ( ) 等是表达式语法，不是字面量。
	/// </summary>
	private IEnumerable<(string Label, string Text)> LiteralInputs()
	{
		switch (Type)
		{
			case RuleType.FindReplace:
				yield return ("“替换为”", _replace);
				break;
			case RuleType.Insert:
				yield return ("“插入内容”", _text);
				break;
			case RuleType.NameTemplate:
				yield return ("“名称模板”", _template);
				break;
			case RuleType.Sequence when _seqPosition != SeqPosition.Replace:
				// 「整体替换为序号」时分隔符不参与名称，此时不必提示
				yield return ("“分隔符”", _separator);
				break;
		}
	}

	/// <summary>Windows 文件名非法字符（GetInvalidFileNameChars 在 Windows 上已含 0x00–0x1F）。</summary>
	private static readonly char[] IllegalNameChars = Path.GetInvalidFileNameChars();

	private static char? FirstIllegalChar(string text)
	{
		foreach (char c in text)
			if (Array.IndexOf(IllegalNameChars, c) >= 0) return c;
		return null;
	}

	/// <summary>控制字符无法直接显示，转成 \uXXXX，否则用户会以为“明明没输入什么”。</summary>
	private static string DescribeChar(char c) => char.IsControl(c) ? $"\\u{(int)c:X4}" : c.ToString();

	/// <summary>实际生效的步长：0 会让整组同名，兜底为 1。</summary>
	[JsonIgnore]
	public long EffectiveStep => _step == 0 ? 1 : _step;

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
		set
		{
			if (SetProperty(ref _seqPosition, value)) RaisePropertyChanged(nameof(InputWarning));
		}
	}

	/// <summary>前/后缀模式中序号与原名之间的分隔文本；留空则紧贴拼接。</summary>
	private string _separator = "_";
	public string Separator
	{
		get => _separator;
		set
		{
			if (SetProperty(ref _separator, value)) RaisePropertyChanged(nameof(InputWarning));
		}
	}

	/// <summary>名称模板规则使用：整段名称由模板构造（{n} = 序号，{name} = 原名）。</summary>
	private string _template = "";
	public string Template
	{
		get => _template;
		set
		{
			if (SetProperty(ref _template, value)) RaisePropertyChanged(nameof(InputWarning));
		}
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
