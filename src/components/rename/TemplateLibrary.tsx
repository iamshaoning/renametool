"use client";

import {
	BookTemplate,
	Brackets,
	Calendar,
	Camera,
	CaseLower,
	Clock,
	Code2,
	Eraser,
	FileText,
	Music,
	Pin,
	Search,
	Star,
	Tag,
	Trash2,
	Tv,
} from "lucide-react";
import { useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
	Dialog,
	DialogContent,
	DialogHeader,
	DialogTitle,
	DialogTrigger,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import {
	Select,
	SelectContent,
	SelectItem,
	SelectTrigger,
	SelectValue,
} from "@/components/ui/select";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { useFilteredPresets, usePresetsStore } from "@/hooks/usePresetsStore";
import type { PresetCategory, PresetSortMode, RuleConfig } from "@/lib/rename/types";

interface Template {
	id: string;
	icon: React.ReactNode;
	rules: RuleConfig[];
	category?: PresetCategory;
}

const TEMPLATE_NAMES: Record<string, { name: string; desc: string }> = {
	removeBrackets: { name: "删除括号及内容", desc: "删除 ()、[]、{} 及其内容" },
	datePrefix: { name: "添加日期前缀", desc: "在文件名前添加 YYYY-MM-DD_" },
	photoCleanup: { name: "照片文件整理", desc: "将 IMG_ 替换为 Photo_ 并添加序号" },
	lowercaseDash: { name: "小写 + 横线", desc: "转为小写并将空格替换为横线" },
	removeExtraSpaces: { name: "删除多余空格", desc: "合并连续空格为一个" },
	tvShowFormat: { name: "影视剧集格式", desc: "格式化剧集编号为 S01E01 - 标题" },
	musicFormat: { name: "音乐曲目格式", desc: "格式化曲目编号为 01. 标题" },
	documentVersion: { name: "文档版本号", desc: "添加版本号后缀如 _v1" },
	downloadCleanup: { name: "下载文件清理", desc: "删除 (1)、copy 和来源标签" },
	camelToKebab: { name: "驼峰转短横线", desc: "转换为 kebab-case 格式" },
	addBackupSuffix: { name: "添加备份后缀", desc: "在文件名后添加 _backup" },
};

function useTemplates() {
	const templates: Template[] = [
		{
			id: "removeBrackets",
			icon: <Brackets className="h-4 w-4" />,
			category: "general",
			rules: [
				{
					type: "regex",
					config: {
						pattern: "\\s*[\\(\\[\\{].*?[\\)\\]\\}]",
						replacement: "",
						flags: "g",
					},
				},
			],
		},
		{
			id: "datePrefix",
			icon: <Calendar className="h-4 w-4" />,
			category: "general",
			rules: [
				{
					type: "insert",
					config: {
						text: `${new Date().toISOString().slice(0, 10)}_`,
						position: "start",
						index: 0,
					},
				},
			],
		},
		{
			id: "photoCleanup",
			icon: <Camera className="h-4 w-4" />,
			category: "photo",
			rules: [
				{
					type: "findReplace",
					config: {
						find: "IMG_",
						replace: "Photo_",
						caseSensitive: false,
						matchAll: true,
						usePosition: false,
						fromEnd: false,
						positionStart: 0,
						positionCount: 1,
					},
				},
				{
					type: "sequence",
					config: {
						seqType: "numeric",
						start: 1,
						step: 1,
						padding: 3,
						position: "end",
						template: "",
						scope: "global",
						sortBeforeNumbering: false,
						sortBy: "name",
						sortOrder: "asc",
						naturalSort: true,
						preserveOriginal: false,
						preservePattern: "(\\d+)",
						hierarchical: false,
						hierarchySeparator: ".",
					},
				},
			],
		},
		{
			id: "lowercaseDash",
			icon: <CaseLower className="h-4 w-4" />,
			category: "code",
			rules: [
				{
					type: "caseStyle",
					config: { mode: "lowercase", style: "spaceToDash" },
				},
			],
		},
		{
			id: "removeExtraSpaces",
			icon: <Eraser className="h-4 w-4" />,
			category: "general",
			rules: [
				{
					type: "regex",
					config: { pattern: "\\s+", replacement: " ", flags: "g" },
				},
			],
		},
		{
			id: "tvShowFormat",
			icon: <Tv className="h-4 w-4" />,
			category: "video",
			rules: [
				{
					type: "regex",
					config: { pattern: "S(\\d{2})E(\\d{2})", replacement: "S$1E$2 -", flags: "i" },
				},
			],
		},
		{
			id: "musicFormat",
			icon: <Music className="h-4 w-4" />,
			category: "music",
			rules: [
				{
					type: "regex",
					config: { pattern: "(\\d+)\\s*-\\s*(.+)", replacement: "$1. $2", flags: "" },
				},
			],
		},
		{
			id: "documentVersion",
			icon: <FileText className="h-4 w-4" />,
			category: "document",
			rules: [
				{
					type: "insert",
					config: { text: "_v", position: "end", index: 0 },
				},
				{
					type: "sequence",
					config: {
						seqType: "numeric",
						start: 1,
						step: 1,
						padding: 1,
						position: "end",
						template: "",
						scope: "global",
						sortBeforeNumbering: false,
						sortBy: "name",
						sortOrder: "asc",
						naturalSort: true,
						preserveOriginal: false,
						preservePattern: "(\\d+)",
						hierarchical: false,
						hierarchySeparator: ".",
					},
				},
			],
		},
		{
			id: "downloadCleanup",
			icon: <Trash2 className="h-4 w-4" />,
			category: "general",
			rules: [
				{
					type: "regex",
					config: {
						pattern: "\\s*\\(\\d+\\)|\\s*-\\s*copy|\\s*-\\s*Copy",
						replacement: "",
						flags: "g",
					},
				},
			],
		},
		{
			id: "camelToKebab",
			icon: <Code2 className="h-4 w-4" />,
			category: "code",
			rules: [
				{
					type: "caseStyle",
					config: { mode: "kebab-case", style: "none" },
				},
			],
		},
		{
			id: "addBackupSuffix",
			icon: <Tag className="h-4 w-4" />,
			category: "general",
			rules: [
				{
					type: "insert",
					config: { text: "_backup", position: "end", index: 0 },
				},
			],
		},
	];

	return templates.map((tmpl) => ({
		...tmpl,
		name: TEMPLATE_NAMES[tmpl.id].name,
		desc: TEMPLATE_NAMES[tmpl.id].desc,
	}));
}

interface Props {
	onApply: (rules: RuleConfig[]) => void;
	trigger: React.ReactNode;
}

export function TemplateLibrary({ onApply, trigger }: Props) {
	const templates = useTemplates();
	const [open, setOpen] = useState(false);

	const presets = useFilteredPresets();
	const pinned = usePresetsStore((state) => state.pinned);
	const sortMode = usePresetsStore((state) => state.sortMode);
	const searchQuery = usePresetsStore((state) => state.searchQuery);
	const categoryFilter = usePresetsStore((state) => state.categoryFilter);
	const setSortMode = usePresetsStore((state) => state.setSortMode);
	const setSearchQuery = usePresetsStore((state) => state.setSearchQuery);
	const setCategoryFilter = usePresetsStore((state) => state.setCategoryFilter);
	const deletePreset = usePresetsStore((state) => state.deletePreset);
	const incrementUsage = usePresetsStore((state) => state.incrementUsage);
	const togglePin = usePresetsStore((state) => state.togglePin);
	const isPinned = usePresetsStore((state) => state.isPinned);

	const handleApply = (rules: RuleConfig[], presetId?: string) => {
		onApply(rules);
		if (presetId) {
			incrementUsage(presetId);
		}
		setOpen(false);
	};

	const handleTogglePin = (type: "system" | "user", id: string) => {
		const wasPinned = isPinned(type, id);
		togglePin(type, id);

		// 提供简短的视觉反馈
		const message = wasPinned ? "已取消固定" : "已固定";

		// 使用简单的临时提示
		const toast = document.createElement("div");
		toast.textContent = message;
		toast.className =
			"fixed bottom-4 left-1/2 -translate-x-1/2 bg-foreground text-background px-4 py-2 rounded-md text-sm shadow-lg z-50 animate-in fade-in slide-in-from-bottom-2 duration-200";
		document.body.appendChild(toast);

		setTimeout(() => {
			toast.classList.add("animate-out", "fade-out", "slide-out-to-bottom-2");
			setTimeout(() => document.body.removeChild(toast), 200);
		}, 1500);
	};

	const pinnedItems = pinned
		.map((p) => {
			if (p.type === "system") {
				return templates.find((t) => t.id === p.id);
			}
			return presets.find((pr) => pr.id === p.id);
		})
		.filter(Boolean);

	return (
		<Dialog open={open} onOpenChange={setOpen}>
			<DialogTrigger asChild>{trigger}</DialogTrigger>
			<DialogContent className="sm:max-w-2xl max-h-[85vh] flex flex-col">
				<DialogHeader>
					<DialogTitle className="flex items-center gap-2">
						<BookTemplate className="h-5 w-5" />
						规则模板
					</DialogTitle>
				</DialogHeader>

				<Tabs defaultValue="pinned" className="flex-1 flex flex-col min-h-0">
					<TabsList className="grid w-full grid-cols-3">
						<TabsTrigger value="pinned" className="flex items-center gap-1.5">
							<Pin className="h-3.5 w-3.5" />
							快速访问
						</TabsTrigger>
						<TabsTrigger value="system">系统模板</TabsTrigger>
						<TabsTrigger value="user">我的预设</TabsTrigger>
					</TabsList>

					<TabsContent value="pinned" className="flex-1 overflow-y-auto mt-4">
						{pinnedItems.length === 0 ? (
							<div className="text-center py-8 text-muted-foreground text-sm">
								<Pin className="h-8 w-8 mx-auto mb-2 opacity-30" />
								暂无固定项。固定模板或预设以便快速访问。
							</div>
						) : (
							<div className="space-y-2">
								{pinnedItems.map((item: any) => {
									const isSystem = "id" in item && templates.some((t) => t.id === item.id);
									const presetId = item.id;
									const pinType = isSystem ? "system" : "user";

									return (
										<PresetCard
											key={presetId}
											name={item.name}
											description={item.desc || item.description}
											icon={item.icon || <Star className="h-4 w-4" />}
											ruleCount={item.rules.length}
											usageCount={item.usageCount}
											lastUsed={item.lastUsedAt}
											tags={item.tags}
											isPinned={true}
											onApply={() => handleApply(item.rules, isSystem ? undefined : presetId)}
										onPin={() => handleTogglePin(pinType, presetId)}
										onDelete={isSystem ? undefined : () => deletePreset(presetId)}
									/>
									);
								})}
							</div>
						)}
					</TabsContent>

					<TabsContent value="system" className="flex-1 overflow-y-auto mt-4">
						<div className="space-y-2">
							{templates.map((tmpl) => (
								<PresetCard
									key={tmpl.id}
									name={tmpl.name}
									description={tmpl.desc}
									icon={tmpl.icon}
									ruleCount={tmpl.rules.length}
									isPinned={isPinned("system", tmpl.id)}
									onApply={() => handleApply(tmpl.rules)}
									onPin={() => handleTogglePin("system", tmpl.id)}
								/>
							))}
						</div>
					</TabsContent>

					<TabsContent value="user" className="flex-1 flex flex-col min-h-0">
						<div className="space-y-3 mb-3">
							<div className="flex gap-2">
								<div className="relative flex-1">
									<Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
									<Input
										placeholder="搜索预设..."
										value={searchQuery}
										onChange={(e) => setSearchQuery(e.target.value)}
										className="pl-9"
									/>
								</div>
								<Select value={sortMode} onValueChange={(v) => setSortMode(v as PresetSortMode)}>
									<SelectTrigger className="w-36">
										<SelectValue />
									</SelectTrigger>
									<SelectContent>
										<SelectItem value="recent">最近使用</SelectItem>
										<SelectItem value="frequent">最常使用</SelectItem>
										<SelectItem value="name">名称</SelectItem>
										<SelectItem value="created">创建时间</SelectItem>
									</SelectContent>
								</Select>
							</div>

							<div className="flex gap-2">
								<Select
									value={categoryFilter}
									onValueChange={(v) => setCategoryFilter(v as PresetCategory | "all")}
								>
									<SelectTrigger className="w-40">
										<SelectValue />
									</SelectTrigger>
									<SelectContent>
										<SelectItem value="all">全部分类</SelectItem>
										<SelectItem value="photo">照片</SelectItem>
										<SelectItem value="document">文档</SelectItem>
										<SelectItem value="code">代码</SelectItem>
										<SelectItem value="video">视频</SelectItem>
										<SelectItem value="music">音乐</SelectItem>
										<SelectItem value="general">通用</SelectItem>
									</SelectContent>
								</Select>

								<div className="flex-1" />
							</div>
						</div>

						<div className="flex-1 overflow-y-auto">
							{presets.length === 0 ? (
								<div className="flex flex-col items-center justify-center h-full py-16 px-6">
									<div className="max-w-md text-center space-y-6">
										<div className="inline-flex items-center justify-center w-16 h-16 rounded-2xl bg-gradient-to-br from-primary/10 to-primary/5 mb-2">
											<Star className="h-8 w-8 text-primary/60" />
										</div>

										<div className="space-y-2">
											<h3 className="text-lg font-semibold">还没有预设</h3>
											<p className="text-sm text-muted-foreground leading-relaxed">
												预设可以保存您常用的规则组合，方便快速应用到不同的文件批次。
											</p>
										</div>

										<div className="pt-4 space-y-4">
											<div className="text-left space-y-3">
												<div className="flex items-center gap-3 text-sm">
													<div className="shrink-0 w-6 h-6 rounded-full bg-primary/10 flex items-center justify-center">
														<span className="text-xs font-semibold text-primary">1</span>
													</div>
													<span className="text-muted-foreground">
														在规则面板配置规则后点击「保存为预设」
													</span>
												</div>
											</div>
										</div>
									</div>
								</div>
							) : (
								<div className="space-y-2">
									{presets.map((preset) => (
										<PresetCard
											key={preset.id}
											name={preset.name}
											description={preset.description}
											icon={<Star className="h-4 w-4" />}
											ruleCount={preset.rules.length}
											usageCount={preset.usageCount}
											lastUsed={preset.lastUsedAt}
											tags={preset.tags}
											isPinned={isPinned("user", preset.id)}
											onApply={() => handleApply(preset.rules, preset.id)}
											onPin={() => handleTogglePin("user", preset.id)}
											onDelete={() => deletePreset(preset.id)}
										/>
									))}
								</div>
							)}
						</div>
					</TabsContent>
				</Tabs>
			</DialogContent>
		</Dialog>
	);
}

interface PresetCardProps {
	name: string;
	description?: string;
	icon: React.ReactNode;
	ruleCount: number;
	usageCount?: number;
	lastUsed?: number;
	tags?: string[];
	isPinned: boolean;
	onApply: () => void;
	onPin: () => void;
	onDelete?: () => void;
}

function PresetCard({
	name,
	description,
	icon,
	ruleCount,
	usageCount,
	lastUsed,
	tags,
	isPinned,
	onApply,
	onPin,
	onDelete,
}: PresetCardProps) {
	const formatLastUsed = (timestamp?: number) => {
		if (!timestamp) return "";
		const diff = Date.now() - timestamp;
		const hours = Math.floor(diff / 3600000);
		const days = Math.floor(diff / 86400000);
		if (hours < 1) return "刚刚";
		if (hours < 24) return `${hours}小时前`;
		return `${days}天前`;
	};

	return (
		<div className="group flex items-start gap-3 rounded-lg border p-3 transition-colors hover:bg-accent/60 hover:border-primary/30">
			<div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-md bg-primary/10 text-primary">
				{icon}
			</div>
			<div className="flex-1 min-w-0">
				<div className="flex items-start justify-between gap-2">
					<div className="flex-1 min-w-0">
						<p className="text-sm font-medium truncate">{name}</p>
						{description && (
							<p className="text-xs text-muted-foreground line-clamp-1">{description}</p>
						)}
						<div className="flex items-center gap-2 mt-1 text-[10px] text-muted-foreground/70">
							<span>
								{ruleCount} 条规则
							</span>
							{usageCount !== undefined && usageCount > 0 && (
								<>
									<span>·</span>
									<span>
										{usageCount} 次使用
									</span>
								</>
							)}
							{lastUsed && (
								<>
									<span>·</span>
									<Clock className="h-3 w-3 inline" />
									<span>{formatLastUsed(lastUsed)}</span>
								</>
							)}
						</div>
						{tags && tags.length > 0 && (
							<div className="flex gap-1 mt-1.5 flex-wrap">
								{tags.map((tag) => (
									<Badge key={tag} variant="secondary" className="text-[10px] px-1.5 py-0">
										{tag}
									</Badge>
								))}
							</div>
						)}
					</div>
					<div className="flex items-center gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
						<Button
							variant="ghost"
							size="sm"
							className={`h-7 w-7 p-0 ${isPinned ? "text-primary" : ""}`}
							onClick={onPin}
						>
							<Pin className="h-3.5 w-3.5" fill={isPinned ? "currentColor" : "none"} />
						</Button>
						{onDelete && (
							<Button
								variant="ghost"
								size="sm"
								className="h-7 w-7 p-0 text-destructive"
								onClick={onDelete}
							>
								<Trash2 className="h-3.5 w-3.5" />
							</Button>
						)}
						<Button variant="default" size="sm" className="h-7 px-2.5 text-xs" onClick={onApply}>
							应用
						</Button>
					</div>
				</div>
			</div>
		</div>
	);
}
