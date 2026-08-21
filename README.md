<div align="center">

# 批量重命名

**浏览器端批量文件重命名工具**

支持规则链、实时预览 — 所有操作本地完成，文件不上传，保护隐私。

在线使用：[https://iamshaoning.github.io/renametool](https://iamshaoning.github.io/renametool)

</div>

---

## ✨ 功能特性

- **100% 本地处理** — 文件通过浏览器 File System Access API 访问，从不离开设备
- **规则链** — 组合多个重命名规则（查找替换、正则替换、插入、序号、大小写/样式、删除/清洗）依次执行
- **实时预览** — 配置规则时即时查看变更，执行前自动检测冲突
- **元数据支持** — 提取图片 EXIF 数据和音频标签用于重命名
- **预设保存** — 将常用规则组合保存为预设，随时复用
- **撤销支持** — 界面内置撤销/重做，误操作可恢复
- **深色模式** — 支持浅色/深色/跟随系统

## 🚀 本地开发

### 环境要求

- Node.js >= 20
- pnpm

### 安装运行

```bash
# 安装依赖
pnpm install

# 启动开发服务器
pnpm dev
```

打开 [http://localhost:3000](http://localhost:3000) 查看应用。

## 📦 脚本命令

| 命令 | 描述 |
|------|------|
| `pnpm dev` | 启动开发服务器 |
| `pnpm build` | 生产环境构建（静态导出到 out/） |
| `pnpm lint` | 使用 Biome 检查代码 |
| `pnpm format` | 使用 Biome 格式化代码 |

## 🔒 隐私保护

- **无上传** — 文件通过浏览器 API 访问，从不传输
- **无服务端处理** — 所有重命名逻辑在客户端运行
- **无数据留存** — 关闭标签页后一切消失
- **无账号** — 无需注册、无追踪

## 📄 许可证

本项目采用 [AGPL-3.0](LICENSE) 许可证。

---

本项目基于 [rename.tools](https://github.com/chenz24/rename.tools) 精简修改，仅保留中文语言与核心重命名功能。
