using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;
using RenameTool.Services;

namespace RenameTool;

/// <summary>关于窗口：程序信息 / 快捷键，顶部两段分页切换。</summary>
public partial class AboutWindow : ToolWindow
{
	/// <summary>一条快捷键说明。</summary>
	public sealed record ShortcutItem(string Keys, string Action);

	private static readonly ShortcutItem[] Items =
	[
		new("Ctrl + O", "导入文件（可多选）"),
		new("Ctrl + Shift + O", "导入文件夹（递归读取其中全部文件）"),
		new("Ctrl + Z", "撤销最近一次改名批次"),
		new("Ctrl + Y", "重做被撤销的改名批次"),
		new("Ctrl + A", "勾选全部文件"),
		new("Ctrl + Shift + A", "取消全部勾选"),
		new("Delete", "移除所选文件（仅移出列表，不动磁盘文件）"),
		new("F5", "刷新预览（重新读取文件状态）"),
		new("Ctrl + Enter", "开始重命名"),
		new("Esc", "关闭弹出窗口 / 二级窗口"),
	];

	public AboutWindow()
	{
		InitializeComponent();

		string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
		VersionText.Text = $"版本 {version}";
		VersionValue.Text = version;
		ShortcutList.ItemsSource = Items;
	}

	/// <summary>点击项目地址，用系统默认浏览器打开。</summary>
	private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
	{
		try
		{
			Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
		}
		catch { /* 无默认浏览器等异常静默忽略 */ }
		e.Handled = true;
	}

	/// <summary>打开数据目录或错误日志（用系统默认程序）。Tag=dir 打开目录，Tag=file 打开日志文件。</summary>
	private void OpenStorage_Click(object sender, RoutedEventArgs e)
	{
		bool isDir = (sender as FrameworkContentElement)?.Tag as string == "dir";
		try
		{
			if (!isDir && File.Exists(AppStorage.ErrorLogFile))
			{
				Process.Start(new ProcessStartInfo(AppStorage.ErrorLogFile) { UseShellExecute = true });
				return;
			}
			// 数据目录：先确保存在再打开；日志尚未生成时退回到其所在目录
			Directory.CreateDirectory(AppStorage.Root);
			Process.Start(new ProcessStartInfo(AppStorage.Root) { UseShellExecute = true });
		}
		catch { /* 资源管理器不可用等异常静默忽略 */ }
	}
}
