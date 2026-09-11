# 批量文件重命名 · RenameTool

一个纯原生的 Windows 桌面批量文件重命名工具，基于 **.NET 8 + WPF** 编写。不依赖任何第三方 NuGet 包，不内置浏览器内核（无 WebView2），发布后为单个可执行文件，开箱即用。

## 功能特性

### 重命名规则（可自由组合、拖动排序、单独启用/停用）

| 规则 | 说明 |
| --- | --- |
| 查找替换 | 按字面或正则查找并替换；正则模式下支持 `$1`、`$2` 反向引用；可仅替换第一处 |
| 插入文本 | 在名称开头 / 末尾 / 指定位置插入文本，支持插入变量模块 |
| 名称模板 | 用变量与固定文本构造整个文件名 |
| 添加序号 | 数字 / 字母 / 罗马数字；全局、按文件夹或按扩展名分组编号；可先排序再编号、可数字感知排序 |
| 大小写处理 | 全部大写 / 全部小写 / 每词首字母大写 / 整句首字母大写；空格、连字符、下划线相互转换 |
| 删除清洗 | 按字符类别清除（数字 / 英文 / 中文 / 空格 / 符号）；从端部删除 N 个字符；删除指定区间 |

### 变量

名称模板、插入文本、查找替换（正则模式）中可插入：

`{n}` 序号 · `{name}` 原文件名 · `{folderName}` 所在文件夹名 · `{relativePath}` 完整路径 · `{date}` 日期 · `{time}` 时间 · `{datetime}` 日期时间 · `{timestamp}` 时间戳 · `{date:自定义格式}` 自定义日期

### 其他

- **实时预览**：重命名前后对照，自动检测冲突、非法名称与空名称；仅对确实有变化的条目可执行
- **文件面板**：按文件夹分组、按名称或扩展名排序、关键字筛选、失效（已删除）条目检测与清理
- **撤销 / 重做**：整批重命名操作可回滚
- **明暗主题**：默认跟随系统，可手动切换并记忆
- **执行日志**：逐条显示成功 / 失败 / 跳过状态
- **应用范围**：每条规则可作用于文件名、扩展名或完整名称

## 环境要求

- 运行：Windows 10 / 11 x64（自包含单文件版无需预装 .NET）
- 构建：.NET 8 SDK

## 构建与运行

```powershell
dotnet run -c Release
```

## 单文件发布

```powershell
dotnet publish -c Release -r win-x64 `
  -p:PublishSingleFile=true `
  -p:SelfContained=true `
  -p:EnableCompressionInSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None -p:DebugSymbols=false
```

产物为单个文件：`bin\Release\net8.0-windows\win-x64\publish\RenameTool.exe`（约 68 MB，含运行时，无需安装 .NET）。

## 项目结构

```
RenameTool.csproj                    项目文件（net8.0-windows / WPF，无第三方依赖）
App.xaml(.cs)                        应用入口、主题加载、全局异常兜底
MainWindow.xaml(.cs)                 主界面：自绘窗口、拖拽排序、数字框联动等交互
ObservableObject.cs / Commands.cs    轻量 MVVM 基类（自实现，未引入 MVVM 框架）
Converters.cs / Formats.cs           值转换器与格式化扩展方法
Models/                              规则参数、文件项、枚举及下拉选项、模板片段
Engine/                              规则应用、序号生成、模板解析、筛选、预览、执行
ViewModels/MainViewModel.cs          主视图模型（导入、预览、命令、撤销重做）
Themes/                              明/暗颜色字典、图标几何、控件样式
Assets/app.ico                       程序图标
app.manifest                         高 DPI 感知、长路径支持
```

## 使用步骤

1. 点击「添加文件」或「添加文件夹」（文件夹会递归导入其中的全部文件）。
2. 在「规则」面板添加需要的规则，拖动调整执行顺序，按需启用或停用。
3. 在预览区查看重命名结果，可切换「全部 / 有变化 / 冲突」视图。
4. 确认无误后点击「执行重命名」；如需回退，使用「撤销」。

## 说明

本项目为原创实现，未使用任何第三方库、框架或浏览器内核。
