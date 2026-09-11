using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using RenameTool.Models;

namespace RenameTool;

/// <summary>文件夹分组 → 该组文件的勾选状态：全选 true / 全不选 false / 部分选中 null（不确定态）。</summary>
public sealed class GroupSelectedConverter : IValueConverter
{
	public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		IEnumerable? items = value switch
		{
			CollectionViewGroup group => group.Items,
			IEnumerable e => e,
			_ => null,
		};
		if (items is null) return false;

		bool any = false, all = true, none = true;
		foreach (object o in items)
		{
			if (o is not FileItem f) continue;
			if (f.IsMissing) continue;   // 失效条目不可选，不参与组勾选状态计算
			any = true;
			if (f.Selected) none = false;
			else all = false;
		}
		if (!any || none) return false;
		return all ? true : null;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		=> Binding.DoNothing;
}

/// <summary>文件夹分组 → 来源标识：整组均为文件夹导入 / 均为文件导入 / 混合。</summary>
public sealed class GroupSourceConverter : IValueConverter
{
	public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		IEnumerable? items = value switch
		{
			CollectionViewGroup group => group.Items,
			IEnumerable e => e,
			_ => null,
		};
		if (items is null) return "";

		bool any = false, anyFolder = false, anyFile = false;
		foreach (object o in items)
		{
			if (o is not FileItem f) continue;
			any = true;
			if (f.IsFolderSource) anyFolder = true;
			else anyFile = true;
		}
		if (!any) return "";
		if (anyFolder && anyFile) return "混合来源";
		return anyFolder ? "文件夹导入" : "文件导入";
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		=> Binding.DoNothing;
}

public sealed class IssueBrushConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		var issue = value is PreviewIssue i ? i : PreviewIssue.None;
		return issue switch
		{
			PreviewIssue.None => Brushes.Transparent,
			PreviewIssue.Conflict => (Brush)Application.Current.FindResource("WarningSoftBrush"),
			_ => (Brush)Application.Current.FindResource("DangerSoftBrush"),
		};
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		=> throw new NotSupportedException();
}

/// <summary>问题徽标文字色（与 IssueBrush 的浅底配对，保证深/浅主题均可读）。</summary>
public sealed class IssueTextBrushConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		var issue = value is PreviewIssue i ? i : PreviewIssue.None;
		return issue switch
		{
			PreviewIssue.Conflict => (Brush)Application.Current.FindResource("WarningBrush"),
			_ => (Brush)Application.Current.FindResource("DangerBrush"),
		};
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		=> throw new NotSupportedException();
}

public sealed class StatusBrushConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		string s = value as string ?? "";
		return s switch
		{
			"success" => (Brush)Application.Current.FindResource("SuccessBrush"),
			"failed" => (Brush)Application.Current.FindResource("DangerBrush"),
			_ => (Brush)Application.Current.FindResource("MutedTextBrush"),
		};
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		=> throw new NotSupportedException();
}

public sealed class StatusTextConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		return (value as string) switch
		{
			"success" => "成功",
			"failed" => "失败",
			_ => "跳过",
		};
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		=> throw new NotSupportedException();
}

/// <summary>规则类型 → Lucide 图标几何（Themes/Icons.xaml 中的资源键）。</summary>
public sealed class RuleIconConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		string key = (value is RuleType t ? t : RuleType.FindReplace) switch
		{
			RuleType.FindReplace => "Icon.Search",
			RuleType.Insert => "Icon.Type",
			RuleType.Sequence => "Icon.ListOrdered",
			RuleType.NameTemplate => "Icon.LayoutTemplate",
			RuleType.CaseStyle => "Icon.CaseSensitive",
			RuleType.RemoveCleanup => "Icon.Eraser",
			_ => "Icon.Square",
		};
		return Application.Current?.TryFindResource(key) as Geometry ?? Geometry.Empty;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		=> throw new NotSupportedException();
}

public sealed class BoolVisibilityConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		bool v = value is bool b && b;
		if (parameter is string p && p == "invert") v = !v;
		return v ? Visibility.Visible : Visibility.Collapsed;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		=> throw new NotSupportedException();
}
