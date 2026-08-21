"use client";

import {
	ArrowDownAZ,
	Database,
	FileUp,
	Filter,
	FolderOpen,
	Loader2,
	Trash2,
	Upload,
} from "lucide-react";
import { useCallback, useEffect, useRef, useState } from "react";
import { FileTree } from "@/components/rename/FileTree";
import { FilterPanel } from "@/components/rename/FilterPanel";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import {
	DropdownMenu,
	DropdownMenuContent,
	DropdownMenuRadioGroup,
	DropdownMenuRadioItem,
	DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { ScrollArea } from "@/components/ui/scroll-area";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import type { MetadataLoadProgress } from "@/hooks/useMetadataLoader";
import type { SortMode } from "@/hooks/useRenameStore";
import type { FileEntry, FilterCondition } from "@/lib/rename/types";

// 递归遍历目录，收集文件名、句柄和相对路径
async function walkDirectory(
	dirHandle: FileSystemDirectoryHandle,
	prefix: string,
	names: string[],
	handles: FileSystemFileHandle[],
	paths: string[],
) {
	for await (const entry of (dirHandle as any).values()) {
		if (entry.kind === "file") {
			names.push(entry.name);
			handles.push(entry);
			paths.push(prefix ? `${prefix}/${entry.name}` : entry.name);
		} else if (entry.kind === "directory") {
			await walkDirectory(
				entry,
				prefix ? `${prefix}/${entry.name}` : entry.name,
				names,
				handles,
				paths,
			);
		}
	}
}

interface Props {
	allFiles: FileEntry[];
	filteredFiles: FileEntry[];
	onAddFiles: (
		names: string[],
		handles?: FileSystemFileHandle[],
		relativePaths?: string[],
	) => Promise<void>;
	onToggle: (id: string) => void;
	onSelectAll: (selected: boolean, filteredIds?: string[]) => void;
	onClear: () => void;
	onSortFiles: (mode: SortMode) => void;
	sortMode: SortMode;
	filterConditions: FilterCondition[];
	filterLogic: "AND" | "OR";
	onAddFilterCondition: () => void;
	onUpdateFilterCondition: (id: string, updates: Partial<FilterCondition>) => void;
	onRemoveFilterCondition: (id: string) => void;
	onSetFilterLogic: (logic: "AND" | "OR") => void;
	onClearFilter: () => void;
	onLoadMetadata?: () => void;
	metadataProgress?: MetadataLoadProgress;
	hasMetadata?: boolean;
}

export function FilePanel({
	allFiles,
	filteredFiles,
	onAddFiles,
	onToggle,
	onSelectAll,
	onClear,
	onSortFiles,
	sortMode,
	filterConditions,
	filterLogic,
	onAddFilterCondition,
	onUpdateFilterCondition,
	onRemoveFilterCondition,
	onSetFilterLogic,
	onClearFilter,
	onLoadMetadata,
	metadataProgress,
	hasMetadata,
}: Props) {
	const [apiSupported, setApiSupported] = useState(false);
	const [filterOpen, setFilterOpen] = useState(false);
	const [isLoadingFiles, setIsLoadingFiles] = useState(false);
	const [loadingFileCount, setLoadingFileCount] = useState(0);

	useEffect(() => {
		setApiSupported("showOpenFilePicker" in window);
	}, []);
	const dropRef = useRef<HTMLDivElement>(null);
	const [dragging, setDragging] = useState(false);

	const importFiles = useCallback(async () => {
		try {
			const handles = await (window as any).showOpenFilePicker({
				multiple: true,
			});
			const names = await Promise.all(handles.map((h: FileSystemFileHandle) => h.name));
			setLoadingFileCount(names.length);
			setIsLoadingFiles(true);
			try {
				await onAddFiles(names, handles);
			} finally {
				setIsLoadingFiles(false);
			}
		} catch {}
	}, [onAddFiles]);

	const importFolder = useCallback(async () => {
		try {
			const dirHandle = await (window as any).showDirectoryPicker({ mode: "readwrite" });
			const rootFolderName = dirHandle.name;
			const names: string[] = [];
			const handles: FileSystemFileHandle[] = [];
			const paths: string[] = [];

			setLoadingFileCount(0);
			setIsLoadingFiles(true);

			try {
				await walkDirectory(dirHandle, rootFolderName, names, handles, paths);
				setLoadingFileCount(names.length);
				await onAddFiles(names, handles, paths);
			} finally {
				setIsLoadingFiles(false);
			}
		} catch {}
	}, [onAddFiles]);

	const handleDrop = useCallback(
		async (e: React.DragEvent) => {
			e.preventDefault();
			setDragging(false);

			// Try to get FileSystemFileHandle for actual rename operations
			const items = Array.from(e.dataTransfer.items);
			if (items.length === 0) return;

			setLoadingFileCount(0);
			setIsLoadingFiles(true);

			try {
				// Check if getAsFileSystemHandle is supported
				if (items[0].getAsFileSystemHandle) {
					const handles: FileSystemFileHandle[] = [];
					const names: string[] = [];
					const paths: string[] = [];

					// Process each dropped item
					for (const item of items) {
						const handle = await item.getAsFileSystemHandle?.();
						if (!handle) continue;

						if (handle.kind === "file") {
							// Single file
							handles.push(handle as FileSystemFileHandle);
							names.push(handle.name);
							paths.push(handle.name);
						} else if (handle.kind === "directory") {
							// Directory - recursively walk it
							const dirHandle = handle as FileSystemDirectoryHandle;
							const rootFolderName = dirHandle.name;
							await walkDirectory(dirHandle, rootFolderName, names, handles, paths);
						}
					}

					if (names.length > 0) {
						setLoadingFileCount(names.length);
						await onAddFiles(names, handles, paths);
						setIsLoadingFiles(false);
						return;
					}
				}
			} catch (error) {
				// Fallback to File API if getAsFileSystemHandle fails
				console.warn("Failed to get file handles, falling back to File API:", error);
			}

			// Fallback: use File API (preview only, cannot rename)
			const files = Array.from(e.dataTransfer.files);
			if (files.length > 0) {
				setLoadingFileCount(files.length);
				await onAddFiles(files.map((f) => f.name));
			}
			setIsLoadingFiles(false);
		},
		[onAddFiles],
	);

	const selectedCount = filteredFiles.filter((f) => f.selected).length;
	const hasActiveFilter = filterConditions.length > 0;

	return (
		<div className="flex h-full flex-col border-r">
			{/* Panel Header + Import Buttons */}
			<div className="border-b bg-muted/20 px-3 py-3 flex items-center gap-2">
				<h2 className="text-sm font-medium text-foreground">文件列表</h2>
				<div className="ml-auto flex items-center gap-1">
					{apiSupported && (
						<>
							<Tooltip>
								<TooltipTrigger asChild>
									<Button size="sm" variant="outline" className="h-7 w-7 p-0" onClick={importFiles}>
										<FileUp className="h-3.5 w-3.5" />
									</Button>
								</TooltipTrigger>
								<TooltipContent>
									<p>导入文件</p>
								</TooltipContent>
							</Tooltip>
							<Tooltip>
								<TooltipTrigger asChild>
									<Button
										size="sm"
										variant="outline"
										className="h-7 w-7 p-0"
										onClick={importFolder}
									>
										<FolderOpen className="h-3.5 w-3.5" />
									</Button>
								</TooltipTrigger>
								<TooltipContent>
									<p>导入文件夹</p>
								</TooltipContent>
							</Tooltip>
						</>
					)}
					{allFiles.length > 0 && onLoadMetadata && (
						<Tooltip>
							<TooltipTrigger asChild>
								<Button
									size="sm"
									variant={hasMetadata ? "default" : "outline"}
									className="h-7 w-7 p-0"
									onClick={onLoadMetadata}
									disabled={metadataProgress?.state === "loading"}
								>
									{metadataProgress?.state === "loading" ? (
										<Loader2 className="h-3.5 w-3.5 animate-spin" />
									) : (
										<Database className="h-3.5 w-3.5" />
									)}
								</Button>
							</TooltipTrigger>
							<TooltipContent>
								<p>
									{metadataProgress?.state === "loading"
										? `正在加载元数据 (${metadataProgress.current}/${metadataProgress.total})`
										: "加载文件元数据（EXIF、音频标签）"}
								</p>
							</TooltipContent>
						</Tooltip>
					)}
					{allFiles.length > 0 && (
						<Tooltip>
							<TooltipTrigger asChild>
								<Button size="sm" variant="ghost" onClick={onClear} className="h-7 w-7 p-0">
									<Trash2 className="h-3.5 w-3.5 text-destructive" />
								</Button>
							</TooltipTrigger>
							<TooltipContent>
								<p>清空</p>
							</TooltipContent>
						</Tooltip>
					)}
				</div>
			</div>

			{/* Drop Zone & File List */}
			<section
				ref={dropRef}
				aria-label="文件列表"
				className={`flex-1 min-h-0 flex flex-col transition-all duration-200 ${
					dragging ? "bg-primary/5 ring-2 ring-inset ring-primary/30" : ""
				}`}
				onDragOver={(e) => {
					e.preventDefault();
					setDragging(true);
				}}
				onDragLeave={() => setDragging(false)}
				onDrop={handleDrop}
			>
				{/* Select All + Sort + Filter */}
				{allFiles.length > 0 && (
					<div className="px-3 py-1.5 border-b">
						<div className="flex items-center gap-2">
							{filteredFiles.length > 0 && (
								// biome-ignore lint/a11y/useSemanticElements: <explanation>
								<div
									role="button"
									tabIndex={0}
									className="inline-flex items-center gap-1.5 text-xs h-7 px-2 cursor-pointer hover:bg-accent hover:text-accent-foreground rounded-md transition-colors"
									onClick={() =>
										onSelectAll(
											selectedCount !== filteredFiles.length,
											filteredFiles.map((f) => f.id),
										)
									}
									onKeyDown={(e) => {
										if (e.key === "Enter" || e.key === " ") {
											e.preventDefault();
											onSelectAll(
												selectedCount !== filteredFiles.length,
												filteredFiles.map((f) => f.id),
											);
										}
									}}
								>
									<Checkbox
										checked={filteredFiles.length > 0 && selectedCount === filteredFiles.length}
										onCheckedChange={() => {}}
									/>
									{selectedCount === filteredFiles.length ? "取消全选" : "全选"}
								</div>
							)}

							<div className="flex-1" />

							{hasActiveFilter && (
								<span className="text-xs text-muted-foreground mr-2">
									{filteredFiles.length}/{allFiles.length}
								</span>
							)}

							<DropdownMenu>
								<DropdownMenuTrigger asChild>
									<Button size="sm" variant="outline" className="gap-1.5 text-xs h-7">
										<ArrowDownAZ className="h-3.5 w-3.5" />
										排序
									</Button>
								</DropdownMenuTrigger>
								<DropdownMenuContent align="end" className="w-48">
									<DropdownMenuRadioGroup
										value={sortMode}
										onValueChange={(v) => onSortFiles(v as SortMode)}
									>
										<DropdownMenuRadioItem value="import">按导入顺序</DropdownMenuRadioItem>
										<DropdownMenuRadioItem value="name-asc">名称 A-Z</DropdownMenuRadioItem>
										<DropdownMenuRadioItem value="name-desc">名称 Z-A</DropdownMenuRadioItem>
										<DropdownMenuRadioItem value="ext-asc">按扩展名 A-Z</DropdownMenuRadioItem>
										<DropdownMenuRadioItem value="ext-desc">按扩展名 Z-A</DropdownMenuRadioItem>
									</DropdownMenuRadioGroup>
								</DropdownMenuContent>
							</DropdownMenu>

							<Popover open={filterOpen} onOpenChange={setFilterOpen}>
								<PopoverTrigger asChild>
									<Button
										size="sm"
										variant={hasActiveFilter ? "default" : "outline"}
										className="gap-1.5 text-xs h-7 relative"
									>
										<Filter className="h-3.5 w-3.5" />
										筛选
										{hasActiveFilter && (
											<Badge
												variant="secondary"
												className="ml-1 h-4 w-4 p-0 flex items-center justify-center text-[10px]"
											>
												{filterConditions.length}
											</Badge>
										)}
									</Button>
								</PopoverTrigger>
								<PopoverContent align="end" className="w-[600px]">
									<FilterPanel
										conditions={filterConditions}
										logic={filterLogic}
										onAddCondition={onAddFilterCondition}
										onUpdateCondition={onUpdateFilterCondition}
										onRemoveCondition={onRemoveFilterCondition}
										onSetLogic={onSetFilterLogic}
										onClearAll={onClearFilter}
									/>
								</PopoverContent>
							</Popover>
						</div>
					</div>
				)}

				{/* Loading State */}
				{isLoadingFiles ? (
					<div className="flex-1 flex items-center justify-center p-6">
						<div className="flex flex-col items-center gap-3 text-center">
							<Loader2 className="h-8 w-8 animate-spin text-primary" />
							<p className="text-sm text-muted-foreground">
								{loadingFileCount > 0
									? `正在加载 ${loadingFileCount} 个文件...`
									: "正在扫描文件..."}
							</p>
						</div>
					</div>
				) : /* Empty State */
				allFiles.length === 0 ? (
					<div className="flex-1 flex items-center justify-center p-6">
						<div
							className={`flex flex-col items-center gap-3 rounded-xl border-2 border-dashed p-6 text-center transition-colors ${
								dragging ? "border-primary bg-primary/5" : "border-muted-foreground/20"
							}`}
						>
							<div className="flex h-12 w-12 items-center justify-center rounded-full bg-primary/10">
								<Upload className="h-6 w-6 text-primary" />
							</div>
							<p className="text-sm text-muted-foreground">
								{apiSupported ? "拖放文件或文件夹到此处" : "暂无文件。导入文件或文件夹以开始。"}
							</p>
							{apiSupported && (
								<div className="flex gap-2 mt-2">
									<Button
										size="sm"
										variant="outline"
										className="gap-1.5 text-xs"
										onClick={importFiles}
									>
										<FileUp className="h-3.5 w-3.5" /> 导入文件
									</Button>
									<Button
										size="sm"
										variant="outline"
										className="gap-1.5 text-xs"
										onClick={importFolder}
									>
										<FolderOpen className="h-3.5 w-3.5" /> 导入文件夹
									</Button>
								</div>
							)}
						</div>
					</div>
				) : (
					<ScrollArea className="flex-1">
						<div className="px-1 py-1">
							<FileTree files={filteredFiles} onToggle={onToggle} />
						</div>
					</ScrollArea>
				)}
			</section>

			{/* Status Bar */}
			<div className="border-t bg-muted/20 px-3 py-1.5 text-xs text-muted-foreground flex justify-between">
				<span>
					总计 {allFiles.length} 个
					{hasActiveFilter && (
						<span className="ml-1 text-primary">(已筛选 {filteredFiles.length})</span>
					)}
				</span>
				<span>已选 {selectedCount} 个</span>
			</div>
		</div>
	);
}
