# 批量文件重命名 · RenameTool

一个纯原生的 Windows 桌面批量文件重命名工具，基于 **.NET 10 + WPF** 编写。不依赖任何第三方 NuGet 包，不内置浏览器内核，可发布为单个 exe。

## 功能特性

### 重命名规则（7 种，可自由组合、拖动排序、单独启用 / 停用）

| 规则 | 说明 |
| --- | --- |
| 查找替换 | 按字面或正则查找并替换；正则模式下支持 `$1`、`$2` 反向引用；可仅替换第一处、可区分大小写 |
| 插入文本 | 在名称开头 / 末尾 / 指定位置插入文本，支持插入变量模块 |
| 添加序号 | 数字 / 字母 / 罗马数字；全局、按文件夹或按扩展名分组编号；可设起始值、步长、位数补零与分隔符；可先排序再编号（升序或反向编号），并支持数字感知的自然排序 |
| 名称模板 | 用变量与固定文本构造整个文件名，变量以可拖拽的模块形式编辑 |
| 大小写处理 | 全部大写 / 全部小写 / 每词首字母大写 / 整句首字母大写；空格、连字符、下划线相互转换 |
| 删除清洗 | 按字符类别清除（数字 / 英文 / 中文 / 空格 / 符号）；从端部删除 N 个字符；删除指定区间 |
| 成对交换 | 把参与本规则的文件按列表顺序配对，互相拿走对方的名称；支持「相邻两两交换」与「整体轮换」（环状改名） |

### 变量

名称模板、插入文本、查找替换（正则模式）中可插入：

`{n}` 序号 · `{name}` 原文件名 · `{ext}` 扩展名 · `{size}` 文件大小 · `{folderName}` 所在文件夹名 · `{parent}` 上级文件夹名 · `{date}` 日期 · `{time}` 时间 · `{datetime}` 日期时间 · `{timestamp}` 时间戳 · `{modified}` 修改日期 · `{created}` 创建日期

### 预览与执行

- **实时预览**：重命名前后对照，自动检测冲突、非法名称与空名称，仅对确实有变化的条目可执行
- **冲突策略**：跳过冲突项 / 自动追加序号 / 整体中止
- **目标名锁定**：可手动锁定某一条的目标名，重算预览时不被规则覆盖
- **执行日志**：逐条显示成功 / 失败 / 跳过状态；失败项可一键重试

### 文件面板

- 按文件夹分组、可折叠展开、支持整组移除
- 按导入顺序排列，或按名称 / 扩展名升序、降序排列
- 关键字筛选，仅筛选结果参与改名
- 失效（已删除）条目检测与清理

### 规则预设与历史

- **规则预设**：把当前规则链存为预设，支持载入、导入 / 导出、删除
- **撤销 / 重做**：整批重命名操作可回滚
- **历史记录**：按批次查看历次改名，可回滚到任意批次

### 界面

- **明暗主题**：跟随系统 / 日间 / 夜间，可手动切换并记忆
- **应用范围**：每条规则可作用于文件名、扩展名或完整名称
- **全局快捷键**：

  | 快捷键 | 操作 |
  | --- | --- |
  | `Ctrl+O` | 添加文件 |
  | `Ctrl+Shift+O` | 添加文件夹 |
  | `Ctrl+Z` / `Ctrl+Y` | 撤销 / 重做 |
  | `Ctrl+A` / `Ctrl+Shift+A` | 全选 / 全不选 |
  | `Delete` | 移除所选 |
  | `F5` | 刷新 |
  | `Ctrl+F` | 定位到筛选框 |
  | `Ctrl+Enter` | 开始重命名 |

- 支持把文件 / 文件夹直接拖进窗口导入
- 单实例运行：已在运行时再次启动会提示并激活已有窗口

## 环境要求

- 运行：Windows 10 / 11 x64（`-full` 自包含版无需预装 .NET；`-min` 版需预装 .NET 10 桌面运行时）
- 构建：.NET 10 SDK

## 构建与运行

```powershell
dotnet run -c Release
```

## 打包发布

在项目根目录执行，一条命令同时产出两个版本：

```powershell
.\publish.ps1
```

| 产物 | 体积 | 说明 |
|---|---|---|
| `dist\RenameTool-<版本>-full.exe` | 约 55 MB | 自包含单文件，内含 .NET 10 运行时，目标机器免安装直接运行 |
| `dist\RenameTool-<版本>-min.exe` | 约 0.5 MB | 框架依赖单文件，体积最小，需目标机器预装 [.NET 10 桌面运行时](https://dotnet.microsoft.com/download/dotnet/10.0) |

脚本会自行定位带 SDK 的 dotnet、从 `.csproj` 读取程序集名与版本号、用 `-o` 显式指定输出目录，因此改版本号、增删源文件、换 TargetFramework / RID 都不需要修改脚本。

若需手动发布，下面两个参数都不能省：

```powershell
dotnet publish -c Release -r win-x64 `
  -p:PublishSingleFile=true `
  -p:SelfContained=true `
  -p:EnableCompressionInSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None -p:DebugSymbols=false
```

- `EnableCompressionInSingleFile` 决定体积：漏掉后 exe 由 55 MB 涨到约 117 MB。
- `IncludeNativeLibrariesForSelfExtract` 决定单文件是否真正独立：漏掉后 `D3DCompiler_47_cor3.dll`、`wpfgfx_cor3.dll`、`PresentationNative_cor3.dll` 等原生库会散落在输出目录，只拷 exe 出去会无法运行。

另外 `.csproj` 中的 `TrimUnusedRuntimePayload` 目标会在单文件发布时剔除程序未引用的运行时组件（`System.Net.Http`、`System.Data.Common`、`ReachFramework` 等），只在 `PublishSingleFile=true` 时生效，普通构建不受影响。

## 项目结构

```
RenameTool.csproj                    项目文件（net10.0-windows / WPF，无第三方依赖）
App.xaml(.cs)                        应用入口、主题加载、单实例互斥、全局异常兜底
MainWindow.xaml(.cs)                 主界面：自绘窗口、拖拽导入与排序、卡片动画等交互
ToolWindow.cs / WindowEffects.cs     窗口基类与阴影、圆角等视觉效果
RowAnimator.cs                       列表行的增删动画
ObservableObject.cs / Commands.cs    轻量 MVVM 基类（自实现，未引入 MVVM 框架）
Converters.cs / Formats.cs           值转换器、尺寸格式化与文本折行工具
AboutWindow / AppDialog              关于窗口与通用对话框
HistoryWindow / PresetWindow         历史记录与规则预设管理
ProgressWindow                       非模态进度提示（导入扫描、批量改名）
Models/                              规则参数、文件项、预览项、文件夹节点、枚举、模板片段
Engine/                              规则应用、序号生成、模板解析、筛选、预览、执行与回滚
Services/                            设置、预设、历史的本地持久化
ViewModels/MainViewModel.cs          主视图模型（导入、预览、命令、撤销重做）
Themes/                              明 / 暗调色板、设计令牌、图标几何、控件样式
Assets/app.ico                       程序图标
app.manifest                         高 DPI 感知（PerMonitorV2）、长路径支持
publish.ps1                          一键打包：自包含版 + 框架依赖最小版
```

配置、预设与历史记录存放在 `%LocalAppData%\RenameTool\` 下（`settings.json` / `presets.json` / `history.json`）。

## 使用步骤

1. 点击「添加文件」或「添加文件夹」（文件夹会递归导入其中的全部文件），也可直接把文件拖进窗口。
2. 在「规则」面板添加需要的规则，拖动调整执行顺序，按需启用或停用。
3. 在预览区查看重命名结果，可切换「全部 / 有变化 / 冲突」视图，并选定冲突处理策略。
4. 确认无误后点击「开始重命名」；如需回退，使用「撤销」或到「历史记录」中回滚。

## 声明

- 本项目的功能与交互由 AI 仿照开源项目 [chenz24/rename.tools](https://github.com/chenz24/rename.tools) 编写，仅用于学习与个人使用。
