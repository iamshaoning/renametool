"use client";

import {
	ChevronRight,
	Eye,
	File,
	Folder,
	FolderOpen,
	Play,
	Redo2,
	RotateCcw,
	Sparkles,
	Undo2,
} from "lucide-react";
import React, { useMemo, useState } from "react";
import { ExecutionLog } from "@/components/rename/ExecutionLog";
import {
	AlertDialog,
	AlertDialogAction,
	AlertDialogCancel,
	AlertDialogContent,
	AlertDialogDescription,
	AlertDialogFooter,
	AlertDialogHeader,
	AlertDialogTitle,
	AlertDialogTrigger,
} from "@/components/ui/alert-dialog";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { ScrollArea } from "@/components/ui/scroll-area";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip";
import type { LogEntry } from "@/hooks/useRenameStore";
import { charDiff, type DiffSegment } from "@/lib/rename/diff";
import type { PreviewResult } from "@/lib/rename/types";

interface Props {
	preview: PreviewResult[];
	isPreviewComputing?: boolean;
	applyAutoFix?: () => void;
	resetAutoFix?: () => void;
	hasAutoFix?: boolean;
	log: LogEntry[];
	progress: { current: number; total: number } | null;
	isExecuting: boolean;
	needsUserActivation: boolean;
	onClearLog: () => void;
	onConfirmActivation: () => void;
	onExecute: () => void;
	onUndo?: () => void;
	onRedo?: () => void;
	canUndo?: boolean;
	canRedo?: boolean;
}

type Filter = "all" | "affected" | "conflicts";

interface PreviewTreeNode {
	name: string;
	path: string;
	items: PreviewResult[];
	children: Map<string, PreviewTreeNode>;
}

function _truncateMiddle(text: string, maxLength: number = 60): string {
	if (text.length <= maxLength) return text;

	const lastDotIndex = text.lastIndexOf(".");
	const hasExtension = lastDotIndex > 0 && lastDotIndex > text.length - 10;

	if (hasExtension) {
		const extension = text.slice(lastDotIndex);
		const nameWithoutExt = text.slice(0, lastDotIndex);
		const availableLength = maxLength - extension.length - 3;

		if (availableLength <= 10) {
			return `${text.slice(0, maxLength - 3)}...`;
		}

		const startLength = Math.ceil(availableLength * 0.6);
		const endLength = Math.floor(availableLength * 0.4);

		return `${nameWithoutExt.slice(0, startLength)}...${nameWithoutExt.slice(-endLength)}${extension}`;
	} else {
		const startLength = Math.ceil((maxLength - 3) * 0.6);
		const endLength = Math.floor((maxLength - 3) * 0.4);
		return `${text.slice(0, startLength)}...${text.slice(-endLength)}`;
	}
}

function DiffOriginal({ segments }: { segments: DiffSegment[] }) {
	return (
		<span>
			{segments.map((seg, i) => {
				if (seg.type === "removed") {
					return (
						<span
							key={`${seg.type}-${i}`}
							className="bg-destructive/20 text-destructive line-through rounded-sm px-px"
						>
							{seg.text}
						</span>
					);
				}
				if (seg.type === "equal") {
					return <span key={`${seg.type}-${i}`}>{seg.text}</span>;
				}
				return null;
			})}
		</span>
	);
}

function DiffNew({ segments }: { segments: DiffSegment[] }) {
	return (
		<span>
			{segments.map((seg, i) => {
				if (seg.type === "added") {
					return (
						<span
							key={`${seg.type}-${i}`}
							className="bg-success/20 text-success font-medium rounded-sm px-[1px]"
						>
							{seg.text}
						</span>
					);
				}
				if (seg.type === "equal") {
					return <span key={`${seg.type}-${i}`}>{seg.text}</span>;
				}
				return null;
			})}
		</span>
	);
}

function buildPreviewTree(items: PreviewResult[]): PreviewTreeNode {
	const root: PreviewTreeNode = {
		name: "",
		path: "",
		items: [],
		children: new Map(),
	};

	for (const item of items) {
		const dir = item.dirPath;
		if (!dir) {
			root.items.push(item);
			continue;
		}
		const parts = dir.split("/");
		let node = root;
		let pathSoFar = "";
		for (const part of parts) {
			pathSoFar = pathSoFar ? `${pathSoFar}/${part}` : part;
			if (!node.children.has(part)) {
				node.children.set(part, {
					name: part,
					path: pathSoFar,
					items: [],
					children: new Map(),
				});
			}
			node = node.children.get(part)!;
		}
		node.items.push(item);
	}

	return root;
}

function countItems(node: PreviewTreeNode): number {
	let count = node.items.length;
	for (const child of node.children.values()) {
		count += countItems(child);
	}
	return count;
}

const PreviewFileItem = React.memo(function PreviewFileItem({
	item,
	depth = 0,
}: {
	item: PreviewResult;
	depth?: number;
}) {
	// 只计算一次 charDiff，供 DiffOriginal 和 DiffNew 共用
	const segments = useMemo(
		() => (item.hasChange ? charDiff(item.original, item.newName) : []),
		[item.hasChange, item.original, item.newName],
	);

	return (
		<div
			className={`grid grid-cols-[auto_minmax(200px,1fr)_auto_minmax(200px,1fr)] gap-2 items-center py-1.5 px-2 text-xs transition-colors ${
				item.conflict ? "bg-destructive/5 border-l-2 border-l-destructive" : "hover:bg-muted/30"
			}`}
		>
			{/* File Icon + Tree Indent */}
			<div className="flex items-center gap-1" style={{ paddingLeft: `${depth * 16}px` }}>
				<File className="h-3.5 w-3.5 shrink-0 text-muted-foreground/60" />
			</div>

			{/* Original Name */}
			<Tooltip>
				<TooltipTrigger asChild>
					<div className="font-mono text-xs relative overflow-hidden">
						<div className="truncate">
							{item.hasChange ? (
								<DiffOriginal segments={segments} />
							) : (
								<span className="text-muted-foreground">{item.original}</span>
							)}
						</div>
					</div>
				</TooltipTrigger>
				<TooltipContent side="top" className="max-w-2xl break-all">
					<p className="font-mono text-xs whitespace-pre-wrap">{item.original}</p>
				</TooltipContent>
			</Tooltip>

			{/* Arrow */}
			<div className="text-center text-muted-foreground/50 shrink-0">→</div>

			{/* New Name */}
			<div className="flex items-center gap-2 min-w-0">
				<Tooltip>
					<TooltipTrigger asChild>
						<div className="font-mono text-xs flex-1 relative overflow-hidden">
							<div className="truncate">
								{item.hasChange ? (
									<DiffNew segments={segments} />
								) : (
									<span className="text-muted-foreground">{item.newName}</span>
								)}
							</div>
						</div>
					</TooltipTrigger>
					<TooltipContent side="top" className="max-w-2xl break-all">
						<p className="font-mono text-xs whitespace-pre-wrap">{item.newName}</p>
					</TooltipContent>
				</Tooltip>

				{/* Conflict Badge */}
				{item.conflict && (
					<Tooltip>
						<TooltipTrigger asChild>
							<Badge variant="destructive" className="text-[10px] py-0 shrink-0">
								{item.error || "冲突"}
							</Badge>
						</TooltipTrigger>
						{item.errorDetail && (
							<TooltipContent side="left" className="max-w-xs break-words">
								<p className="text-xs">{item.errorDetail}</p>
							</TooltipContent>
						)}
					</Tooltip>
				)}
			</div>
		</div>
	);
});

function FolderNodeView({ node, depth }: { node: PreviewTreeNode; depth: number }) {
	const [open, setOpen] = useState(true);
	const totalItems = useMemo(() => countItems(node), [node]);

	return (
		<div className="relative">
			{/* Tree guide line */}
			{depth > 0 && (
				<div
					className="absolute top-0 bottom-0 border-l border-border/40"
					style={{ left: `${depth * 16 + 3}px` }}
				/>
			)}
			<button
				type="button"
				className="flex items-center gap-1.5 w-full py-1 text-xs hover:bg-accent/50 rounded-md transition-colors group"
				style={{ paddingLeft: `${depth * 16 + 6}px`, paddingRight: "8px" }}
				onClick={() => setOpen(!open)}
			>
				<ChevronRight
					className={`h-3.5 w-3.5 shrink-0 text-muted-foreground/70 transition-transform duration-200 ${
						open ? "rotate-90" : ""
					}`}
				/>
				{open ? (
					<FolderOpen className="h-4 w-4 shrink-0 text-primary" />
				) : (
					<Folder className="h-4 w-4 shrink-0 text-muted-foreground" />
				)}
				<span className="truncate font-medium text-foreground/90 group-hover:text-foreground">
					{node.name}
				</span>
				<span className="ml-auto rounded-full bg-muted/60 px-1.5 py-px text-[10px] text-muted-foreground tabular-nums shrink-0">
					{totalItems}
				</span>
			</button>
			{open && (
				<div className="relative">
					<FolderContentsView node={node} depth={depth + 1} />
				</div>
			)}
		</div>
	);
}

function FolderContentsView({ node, depth }: { node: PreviewTreeNode; depth: number }) {
	const sortedChildren = useMemo(
		() => Array.from(node.children.values()).sort((a, b) => a.name.localeCompare(b.name)),
		[node.children],
	);

	return (
		<>
			{sortedChildren.map((child) => (
				<FolderNodeView key={child.path} node={child} depth={depth} />
			))}
			{node.items.map((item) => (
				<div key={item.fileId} className="relative">
					{depth > 0 && (
						<div
							className="absolute top-0 bottom-0 border-l border-border/40"
							style={{ left: `${depth * 16 + 3}px` }}
						/>
					)}
					<PreviewFileItem item={item} depth={depth} />
				</div>
			))}
		</>
	);
}

export function PreviewPanel({
	preview,
	isPreviewComputing = false,
	applyAutoFix,
	resetAutoFix,
	hasAutoFix,
	log,
	progress,
	isExecuting,
	needsUserActivation,
	onClearLog,
	onConfirmActivation,
	onExecute,
	onUndo,
	onRedo,
	canUndo,
	canRedo,
}: Props) {
	const [filter, setFilter] = useState<Filter>("all");
	const [warningChecked, setWarningChecked] = useState(false);

	const affected = preview.filter((r) => r.hasChange);
	const conflicts = preview.filter((r) => r.conflict);

	const displayed = filter === "all" ? preview : filter === "affected" ? affected : conflicts;

	const filters: { key: Filter; label: string; count: number }[] = [
		{ key: "all", label: "全部", count: preview.length },
		{ key: "affected", label: "仅受影响", count: affected.length },
		{
			key: "conflicts",
			label: "仅冲突",
			count: conflicts.length,
		},
	];

	const previewTree = useMemo(() => buildPreviewTree(displayed), [displayed]);
	const hasTree = previewTree.children.size > 0;

	const affectedCount = affected.length;
	const hasConflicts = conflicts.length > 0;

	const showLog = progress !== null || log.length > 0;

	return (
		<div className="flex h-full flex-col">
			{/* Header */}
			<div className="panel-header border-b bg-muted/30 px-4 flex items-center justify-between py-3!">
				<div className="flex items-center gap-2">
					<Eye className="h-4 w-4 text-primary" />
					<h2 className="text-foreground">预览</h2>
					{conflicts.length > 0 && applyAutoFix && (
						<div className="flex gap-1.5 ml-1">
							{!hasAutoFix ? (
								<Button
									type="button"
									variant="outline"
									size="sm"
									onClick={applyAutoFix}
									className="h-6 px-2 text-xs gap-1.5"
								>
									<Sparkles className="h-3 w-3" />
									自动修复
								</Button>
							) : (
								<Button
									type="button"
									variant="ghost"
									size="sm"
									onClick={resetAutoFix}
									className="h-6 px-2 text-xs gap-1.5"
								>
									<RotateCcw className="h-3 w-3" />
									重置修复
								</Button>
							)}
						</div>
					)}
				</div>
				<div className="flex gap-0.5 rounded-full bg-muted p-0.5">
					{filters.map((f) => (
						<button
							type="button"
							key={f.key}
							onClick={() => setFilter(f.key)}
							className={`rounded-full px-2.5 py-1 text-xs font-medium transition-all ${
								filter === f.key
									? "bg-card text-foreground shadow-sm"
									: "text-muted-foreground hover:text-foreground"
							}`}
						>
							{f.label}
							{f.count > 0 && (
								<span
									className={`ml-1 text-[10px] ${
										f.key === "conflicts" && f.count > 0 ? "text-destructive" : ""
									}`}
								>
									{f.count}
								</span>
							)}
						</button>
					))}
				</div>
			</div>

			{/* Table Header */}
			<div className="border-b bg-muted/30 sticky top-0 z-10">
				<div className="grid grid-cols-[auto_minmax(200px,1fr)_auto_minmax(200px,1fr)] gap-2 px-2 py-3 text-xs font-medium text-muted-foreground">
					<div className="flex items-center gap-1">
						<span className="w-3.5" />
					</div>
					<div>原始名</div>
					<div className="text-center w-8">→</div>
					<div>新名称</div>
				</div>
			</div>

			{/* File List */}
			<ScrollArea className="flex-1">
				<TooltipProvider delayDuration={200}>
					<div className="px-1 py-1">
						{displayed.length === 0 ? (
							<div className="flex flex-col items-center justify-center py-12 text-muted-foreground">
								<Eye className="h-8 w-8 text-muted-foreground/30 mb-2" />
								<span className="text-xs">{preview.length === 0 ? "—" : "无冲突"}</span>
							</div>
						) : !hasTree ? (
							<div className="space-y-px">
								{displayed.map((item) => (
									<PreviewFileItem key={item.fileId} item={item} />
								))}
							</div>
						) : (
							<div className="space-y-px">
								<FolderContentsView node={previewTree} depth={0} />
							</div>
						)}
					</div>
				</TooltipProvider>
			</ScrollArea>

			{/* Execution Log */}
			{showLog && (
				<ExecutionLog
					log={log}
					progress={progress}
					isExecuting={isExecuting}
					needsUserActivation={needsUserActivation}
					onConfirmActivation={onConfirmActivation}
					onClear={onClearLog}
				/>
			)}

			{/* Bottom Action Bar */}
			<div className="flex items-center gap-2 border-t bg-card px-4 py-2 shadow-[0_-2px_10px_hsl(var(--border)/0.5)]">
				<div className="ml-auto flex items-center gap-2">
					{onUndo && (
						<Button
							variant="outline"
							size="sm"
							className="gap-1 text-xs"
							onClick={onUndo}
							disabled={!canUndo || isExecuting}
							title="撤销上次批量重命名"
						>
							<Undo2 className="h-3.5 w-3.5" /> 撤销
						</Button>
					)}
					{onRedo && (
						<Button
							variant="outline"
							size="sm"
							className="gap-1 text-xs"
							onClick={onRedo}
							disabled={!canRedo || isExecuting}
							title="重做上次撤销的操作"
						>
							<Redo2 className="h-3.5 w-3.5" /> 重做
						</Button>
					)}
					<AlertDialog>
						<AlertDialogTrigger asChild>
							<button
								type="button"
								className="inline-flex items-center gap-1.5 rounded-md brand-gradient px-4 py-2 text-sm font-medium text-white shadow-sm transition-all hover:shadow-lg disabled:opacity-50 disabled:pointer-events-none"
								disabled={affectedCount === 0 || hasConflicts || isExecuting || isPreviewComputing}
							>
								<Play className="h-3.5 w-3.5" />
								执行重命名 ({affectedCount})
							</button>
						</AlertDialogTrigger>
						<AlertDialogContent>
							<AlertDialogHeader>
								<AlertDialogTitle>确认重命名</AlertDialogTitle>
								<AlertDialogDescription>
									即将重命名 {affectedCount} 个文件。
								</AlertDialogDescription>
							</AlertDialogHeader>

							<div className="flex items-start gap-2 rounded-md border border-warning/30 bg-warning/5 p-3">
								<Checkbox
									id="timestamp-warning"
									checked={warningChecked}
									onCheckedChange={(v) => setWarningChecked(!!v)}
									className="mt-0.5"
								/>
								<label
									htmlFor="timestamp-warning"
									className="text-xs text-muted-foreground leading-relaxed cursor-pointer"
								>
									我已了解：重命名将更改文件修改时间戳
								</label>
							</div>

							<AlertDialogFooter>
								<AlertDialogCancel onClick={() => setWarningChecked(false)}>
									取消
								</AlertDialogCancel>
								<AlertDialogAction
									onClick={() => {
										onExecute();
										setWarningChecked(false);
									}}
									disabled={!warningChecked}
							>
								确认
							</AlertDialogAction>
							</AlertDialogFooter>
						</AlertDialogContent>
					</AlertDialog>
				</div>
			</div>
		</div>
	);
}
