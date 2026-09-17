using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RenameTool;

/// <summary>对话框图标种类（决定图标几何与配色）。</summary>
public enum DialogIcon { Info, Warning, Error, Question }

/// <summary>对话框按钮样式。</summary>
public enum DialogButtonStyle { Default, Primary, Danger }

/// <summary>对话框按钮定义。</summary>
public sealed record DialogButton(string Text, DialogButtonStyle Style = DialogButtonStyle.Default);

/// <summary>
/// 应用统一的风格化提示 / 确认对话框，替代系统 MessageBox：
/// 与主窗口一致的无边框自绘样式（细边框、圆角、主题配色、macOS 风格按钮）。
/// </summary>
public partial class AppDialog : ToolWindow
{
	private int _result = -1;

	private AppDialog(string title, string message, DialogIcon icon)
	{
		InitializeComponent();
		TitleText.Text = title;
		// 正文可用宽度约 540（对话框 580 − 左右各 20 内边距），字号 13 时一行约 41 个全角字；
		// 取 40 留出余量（英文与个别宽字形会比估算更宽），再由 TextFlow 均衡折行，
		// 避免出现「第一行塞满、第二行只剩几个字」的排布
		MessageText.Text = TextFlow.Balance(message, 40);
		ApplyIcon(icon);
	}

	/// <summary>把当前对话框前置到主窗口之上（作为其所有者窗口）。</summary>
	private static void AttachOwner(AppDialog dialog, Window? owner)
	{
		owner ??= Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
			?? Application.Current?.MainWindow;
		if (owner is not null && owner != dialog && owner.IsLoaded) dialog.Owner = owner;
		if (dialog.Owner is null) dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
	}

	/// <summary>按图标种类设置图标几何与配色（强调色 / 警告色 / 危险色）。</summary>
	private void ApplyIcon(DialogIcon icon)
	{
		(string geo, string stroke, string fill) = icon switch
		{
			DialogIcon.Warning => ("Icon.AlertTriangle", "WarningBrush", "WarningSoftBrush"),
			DialogIcon.Error => ("Icon.X", "DangerBrush", "DangerSoftBrush"),
			DialogIcon.Question => ("Icon.Help", "AccentBrush", "AccentSoftBrush"),
			_ => ("Icon.Info", "AccentBrush", "AccentSoftBrush"),
		};
		IconGlyph.Data = (Geometry)FindResource(geo);
		IconGlyph.Stroke = (Brush)FindResource(stroke);
		IconWrap.Background = (Brush)FindResource(fill);
	}

	/// <summary>按传入顺序生成按钮，返回需要初始聚焦的按钮。</summary>
	private Button AddButtons(IReadOnlyList<DialogButton> buttons)
	{
		Button? focus = null;
		for (int i = 0; i < buttons.Count; i++)
		{
			DialogButton def = buttons[i];
			var button = new Button
			{
				Content = def.Text,
				Height = 32,
				MinHeight = 0,
				Padding = new Thickness(18, 0, 18, 0),
				Margin = i == 0 ? new Thickness(0) : new Thickness(8, 0, 0, 0),
			};
			if (def.Style == DialogButtonStyle.Primary) button.Style = (Style)FindResource("PrimaryButton");
			else if (def.Style == DialogButtonStyle.Danger) button.Style = (Style)FindResource("DangerButton");

			int index = i;
			button.Click += (_, _) => { _result = index; DialogResult = true; };
			ButtonPanel.Children.Add(button);
			// 危险按钮不作为默认焦点（避免回车误触破坏性操作）
			if (i == 0 && def.Style != DialogButtonStyle.Danger) focus = button;
		}
		return focus ?? (Button)ButtonPanel.Children[0];
	}

	private int Run(IReadOnlyList<DialogButton> buttons, object? extra)
	{
		if (extra is not null) ExtraHost.Content = extra;
		Button focus = AddButtons(buttons);
		Loaded += (_, _) => focus.Focus();
		return ShowDialog() == true ? _result : -1;
	}

	// ─────────────── 静态入口 ───────────────

	/// <summary>纯提示（仅一个确定按钮）。</summary>
	public static void Notify(Window? owner, string title, string message, DialogIcon icon = DialogIcon.Info)
	{
		var dialog = new AppDialog(title, message, icon);
		AttachOwner(dialog, owner);
		dialog.Run([new DialogButton("确定", DialogButtonStyle.Primary)], null);
	}

	/// <summary>错误提示。</summary>
	public static void Error(Window? owner, string title, string message)
		=> Notify(owner, title, message, DialogIcon.Error);

	/// <summary>
	/// 二次确认。<paramref name="danger"/> 为 true 时按钮顺序为「取消 / 危险确认」且默认焦点在取消。
	/// </summary>
	public static bool Confirm(Window? owner, string title, string message, string okText = "确定",
		string cancelText = "取消", DialogIcon icon = DialogIcon.Question, bool danger = false)
	{
		var dialog = new AppDialog(title, message, icon);
		AttachOwner(dialog, owner);
		IReadOnlyList<DialogButton> buttons = danger
			? [new DialogButton(cancelText), new DialogButton(okText, DialogButtonStyle.Danger)]
			: [new DialogButton(okText, DialogButtonStyle.Primary), new DialogButton(cancelText)];
		int okIndex = danger ? 1 : 0;
		return dialog.Run(buttons, null) == okIndex;
	}

	/// <summary>
	/// 改名执行被中断后的收尾询问：还原已完成的改动，还是保留这部分改动。
	/// 直接关闭窗口（返回 -1）等同于「保留」，避免误触触发批量还原。返回 true 表示选择还原。
	/// </summary>
	public static bool AskRestoreAfterInterrupt(Window? owner, int done, int total)
	{
		string message =
			$"改名已被中断：本次计划 {total} 项，已完成 {done} 项。\n\n" +
			"还原：把已完成的改动全部退回原名；\n" +
			"保留：保持这部分已完成的结果。";

		var dialog = new AppDialog("改名已中断", message, DialogIcon.Warning);
		AttachOwner(dialog, owner);

		IReadOnlyList<DialogButton> buttons =
		[
			new DialogButton("保留已完成的更改", DialogButtonStyle.Primary),
			new DialogButton("还原已完成的更改"),
		];
		return dialog.Run(buttons, null) == 1;
	}

	/// <summary>
	/// 预设同名冲突：展示「现有 / 导入」对比，并让用户选择 覆盖 / 改名另存 / 跳过。
	/// 返回：0 = 覆盖，1 = 改名另存（新名称见 <c>NewName</c>），2 或窗口关闭 = 跳过。
	/// </summary>
	public static (int Choice, string NewName) ResolveNameConflict(
		string presetName, string existingSummary, string importedSummary, string suggestedNewName)
	{
		string message =
			$"已存在同名预设「{presetName}」。\n\n" +
			$"现有：{existingSummary}\n" +
			$"导入：{importedSummary}\n\n" +
			"请选择处理方式：覆盖现有预设、以新名称另存导入的预设，或跳过该条。";

		var dialog = new AppDialog("预设名称冲突", message, DialogIcon.Warning);
		AttachOwner(dialog, null);

		var nameBox = new TextBox
		{
			Text = suggestedNewName,
			MinHeight = 30,
			Margin = new Thickness(0, 4, 0, 0),
		};
		var extra = new StackPanel();
		extra.Children.Add(new TextBlock { Text = "另存名称", Style = (Style)dialog.FindResource("FieldLabel") });
		extra.Children.Add(nameBox);

		IReadOnlyList<DialogButton> buttons =
		[
			new DialogButton("覆盖", DialogButtonStyle.Primary),
			new DialogButton("改名另存"),
			new DialogButton("跳过"),
		];
		int choice = dialog.Run(buttons, extra);
		string newName = nameBox.Text.Trim();
		if (newName.Length == 0) newName = suggestedNewName;
		return (choice, newName);
	}

	/// <summary>
	/// 回滚前的对照预览与二次确认：以「当前名称 → 回滚后名称」的清单展示该批次全部改动，
	/// 用户确认后才真正执行回滚。<paramref name="pairs"/> 的 From 为当前名、To 为回滚目标名。
	/// </summary>
	public static bool ConfirmRollback(Window? owner, string summary, IReadOnlyList<(string From, string To)> pairs)
	{
		var dialog = new AppDialog("确认回滚", summary, DialogIcon.Warning);
		AttachOwner(dialog, owner);

		var list = new StackPanel();
		const int maxRows = 50;
		int shown = Math.Min(pairs.Count, maxRows);
		for (int i = 0; i < shown; i++)
		{
			(string from, string to) = pairs[i];
			var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
			row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

			var left = new TextBlock
			{
				Text = from,
				FontSize = 12,
				TextTrimming = TextTrimming.CharacterEllipsis,
				Foreground = (Brush)dialog.FindResource("MutedTextBrush"),
				VerticalAlignment = VerticalAlignment.Center,
				ToolTip = from,
			};
			var arrow = new TextBlock
			{
				Text = "→",
				FontSize = 12,
				Margin = new Thickness(8, 0, 8, 0),
				Foreground = (Brush)dialog.FindResource("FaintTextBrush"),
				VerticalAlignment = VerticalAlignment.Center,
			};
			var right = new TextBlock
			{
				Text = to,
				FontSize = 12,
				TextTrimming = TextTrimming.CharacterEllipsis,
				Foreground = (Brush)dialog.FindResource("TextBrush"),
				VerticalAlignment = VerticalAlignment.Center,
				ToolTip = to,
			};
			Grid.SetColumn(left, 0);
			Grid.SetColumn(arrow, 1);
			Grid.SetColumn(right, 2);
			MakeNameExpandable(left, from);
			MakeNameExpandable(right, to);
			row.Children.Add(left);
			row.Children.Add(arrow);
			row.Children.Add(right);
			list.Children.Add(row);
		}
		if (pairs.Count > maxRows)
		{
			list.Children.Add(new TextBlock
			{
				Text = $"…… 其余 {pairs.Count - maxRows} 项未列出",
				FontSize = 11.5,
				Margin = new Thickness(0, 4, 0, 0),
				Foreground = (Brush)dialog.FindResource("FaintTextBrush"),
			});
		}

		var box = new Border
		{
			Background = (Brush)dialog.FindResource("SurfaceBrush"),
			BorderBrush = (Brush)dialog.FindResource("BorderBrush"),
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(6),
			Padding = new Thickness(12, 10, 12, 10),
			Child = new ScrollViewer
			{
				// 尽量把整批文件一次列完，只有批次特别大时才出现滚动条
				MaxHeight = 340,
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
				Content = list,
			},
		};

		IReadOnlyList<DialogButton> buttons =
		[
			new DialogButton("取消"),
			new DialogButton("确认回滚", DialogButtonStyle.Danger),
		];
		return dialog.Run(buttons, box) == 1;
	}

	/// <summary>
	/// 给对话框里的文件名文本加上「点击展开 / 收起完整文件名」的交互，
	/// 与主窗口文件列表、历史记录等处的行为保持一致。
	/// </summary>
	private static void MakeNameExpandable(TextBlock text, string fullName)
	{
		text.Cursor = Cursors.Hand;
		text.MouseLeftButtonUp += (_, _) =>
		{
			bool expand = text.TextWrapping == TextWrapping.NoWrap;
			text.TextWrapping = expand ? TextWrapping.Wrap : TextWrapping.NoWrap;
			text.TextTrimming = expand ? TextTrimming.None : TextTrimming.CharacterEllipsis;
			text.ToolTip = expand ? null : fullName;   // 展开后已完整显示，无需悬停提示
		};
	}

	// ─────────────── 窗口交互 ───────────────

	/// <summary>按 ESC 等同「取消」：不直接关窗，而是走 DialogResult，让调用方拿到 -1。</summary>
	protected override void Window_PreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key != Key.Escape) return;
		_result = -1;
		DialogResult = false;
	}
}
