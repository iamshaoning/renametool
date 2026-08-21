"use client";

import {
	closestCenter,
	DndContext,
	type DragEndEvent,
	KeyboardSensor,
	PointerSensor,
	useSensor,
	useSensors,
} from "@dnd-kit/core";
import {
	arrayMove,
	horizontalListSortingStrategy,
	SortableContext,
	sortableKeyboardCoordinates,
	useSortable,
} from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { GripVertical, Type, Variable, X } from "lucide-react";
import { useCallback, useId, useMemo, useState } from "react";
import { Badge } from "@/components/ui/badge";
import {
	DropdownMenu,
	DropdownMenuContent,
	DropdownMenuItem,
	DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Input } from "@/components/ui/input";

export interface TemplateBlock {
	id: string;
	type: "variable" | "text";
	value: string;
}

interface TemplateEditorProps {
	value: string;
	onChange: (value: string) => void;
	variables: { key: string; label: string; category?: string }[];
	placeholder?: string;
}

const VARIABLE_REGEX = /\{([^}]+)\}/g;

function parseTemplate(template: string): TemplateBlock[] {
	const blocks: TemplateBlock[] = [];
	let lastIndex = 0;
	let match: RegExpExecArray | null;
	let id = 0;

	const regex = new RegExp(VARIABLE_REGEX.source, "g");
	while (true) {
		match = regex.exec(template);
		if (match === null) break;
		if (match.index > lastIndex) {
			blocks.push({
				id: `text-${id++}`,
				type: "text",
				value: template.slice(lastIndex, match.index),
			});
		}
		blocks.push({
			id: `var-${id++}`,
			type: "variable",
			value: match[1],
		});
		lastIndex = regex.lastIndex;
	}

	if (lastIndex < template.length) {
		blocks.push({
			id: `text-${id++}`,
			type: "text",
			value: template.slice(lastIndex),
		});
	}

	return blocks;
}

function blocksToTemplate(blocks: TemplateBlock[]): string {
	return blocks.map((b) => (b.type === "variable" ? `{${b.value}}` : b.value)).join("");
}

function SortableBlock({
	block,
	onRemove,
	onEdit,
}: {
	block: TemplateBlock;
	onRemove: () => void;
	onEdit: (newValue: string) => void;
}) {
	const [isEditing, setIsEditing] = useState(false);
	const [editValue, setEditValue] = useState(block.value);
	const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
		id: block.id,
	});

	const style = {
		transform: CSS.Transform.toString(transform),
		transition,
		zIndex: isDragging ? 10 : 0,
	};

	const handleSave = () => {
		if (editValue.trim() && editValue !== block.value) {
			onEdit(editValue.trim());
		} else {
			setEditValue(block.value);
		}
		setIsEditing(false);
	};

	if (block.type === "variable") {
		return (
			<div
				ref={setNodeRef}
				style={style}
				className={`inline-flex items-center ${isDragging ? "opacity-80 scale-105" : ""}`}
			>
				<div className="flex items-center gap-0.5 h-6 pl-1.5 pr-1 rounded-md bg-primary/15 border border-primary/25 text-primary text-[11px] font-mono shadow-sm">
					<GripVertical
						className="h-3 w-3 text-primary/40 cursor-grab active:cursor-grabbing shrink-0"
						{...attributes}
						{...listeners}
					/>
					<span className="px-0.5">{`{${block.value}}`}</span>
					<button
						type="button"
						onClick={onRemove}
						className="p-0.5 hover:bg-primary/20 rounded transition-colors"
					>
						<X className="h-3 w-3" />
					</button>
				</div>
			</div>
		);
	}

	// Text block - editable
	if (isEditing) {
		return (
			<div ref={setNodeRef} style={style} className="inline-flex items-center">
				<Input
					autoFocus
					className="h-6 w-24 text-xs px-2 border-primary/30 focus-visible:ring-1"
					value={editValue}
					onChange={(e) => setEditValue(e.target.value)}
					onBlur={handleSave}
					onKeyDown={(e) => {
						if (e.key === "Enter") handleSave();
						if (e.key === "Escape") {
							setEditValue(block.value);
							setIsEditing(false);
						}
					}}
				/>
			</div>
		);
	}

	return (
		<div
			ref={setNodeRef}
			style={style}
			className={`inline-flex items-center ${isDragging ? "opacity-80 scale-105" : ""}`}
		>
			<div className="flex items-center gap-0.5 h-6 pl-1.5 pr-1 rounded-md bg-muted border border-border text-foreground/80 text-[11px] shadow-sm">
				<GripVertical
					className="h-3 w-3 text-muted-foreground/40 cursor-grab active:cursor-grabbing shrink-0"
					{...attributes}
					{...listeners}
				/>
				<button
					type="button"
					className="px-0.5 max-w-25 truncate text-left hover:text-foreground"
					onClick={() => setIsEditing(true)}
				>
					{block.value || "..."}
				</button>
				<button
					type="button"
					onClick={onRemove}
					className="p-0.5 hover:bg-accent rounded transition-colors"
				>
					<X className="h-3 w-3 text-muted-foreground" />
				</button>
			</div>
		</div>
	);
}

export function TemplateEditor({ value, onChange, variables, placeholder }: TemplateEditorProps) {
	const instanceId = useId();
	const [isAddingText, setIsAddingText] = useState(false);
	const [newText, setNewText] = useState("");

	const blocks = useMemo(() => parseTemplate(value), [value]);

	const sensors = useSensors(
		useSensor(PointerSensor, {
			activationConstraint: { distance: 5 },
		}),
		useSensor(KeyboardSensor, {
			coordinateGetter: sortableKeyboardCoordinates,
		}),
	);

	const handleDragEnd = useCallback(
		(event: DragEndEvent) => {
			const { active, over } = event;
			if (over && active.id !== over.id) {
				const oldIndex = blocks.findIndex((b) => b.id === active.id);
				const newIndex = blocks.findIndex((b) => b.id === over.id);
				const newBlocks = arrayMove(blocks, oldIndex, newIndex);
				onChange(blocksToTemplate(newBlocks));
			}
		},
		[blocks, onChange],
	);

	const handleRemoveBlock = useCallback(
		(id: string) => {
			const newBlocks = blocks.filter((b) => b.id !== id);
			onChange(blocksToTemplate(newBlocks));
		},
		[blocks, onChange],
	);

	const handleEditBlock = useCallback(
		(id: string, newValue: string) => {
			const newBlocks = blocks.map((b) => (b.id === id ? { ...b, value: newValue } : b));
			onChange(blocksToTemplate(newBlocks));
		},
		[blocks, onChange],
	);

	const handleAddVariable = useCallback(
		(varKey: string) => {
			const newBlock: TemplateBlock = {
				id: `var-${Date.now()}`,
				type: "variable",
				value: varKey,
			};
			onChange(blocksToTemplate([...blocks, newBlock]));
		},
		[blocks, onChange],
	);

	const handleAddText = useCallback(() => {
		if (newText.trim()) {
			const newBlock: TemplateBlock = {
				id: `text-${Date.now()}`,
				type: "text",
				value: newText,
			};
			onChange(blocksToTemplate([...blocks, newBlock]));
			setNewText("");
		}
		setIsAddingText(false);
	}, [blocks, onChange, newText]);

	const groupedVariables = useMemo(() => {
		const groups: Record<string, typeof variables> = {};
		for (const v of variables) {
			const cat = v.category || "common";
			if (!groups[cat]) groups[cat] = [];
			groups[cat].push(v);
		}
		return groups;
	}, [variables]);

	return (
		<div className="space-y-2">
			{/* Blocks area - main editor */}
			<div className="min-h-10 p-2 border rounded-lg bg-muted/30 flex flex-wrap items-center gap-1.5 transition-colors focus-within:border-primary/50 focus-within:bg-background">
				{blocks.length === 0 && !isAddingText && (
					<span className="text-xs text-muted-foreground/60 px-1 select-none">
						{placeholder || "点击下方按钮添加变量或文本..."}
					</span>
				)}
				<DndContext
					id={instanceId}
					sensors={sensors}
					collisionDetection={closestCenter}
					onDragEnd={handleDragEnd}
				>
					<SortableContext items={blocks.map((b) => b.id)} strategy={horizontalListSortingStrategy}>
						{blocks.map((block) => (
							<SortableBlock
								key={block.id}
								block={block}
								onRemove={() => handleRemoveBlock(block.id)}
								onEdit={(newValue) => handleEditBlock(block.id, newValue)}
							/>
						))}
					</SortableContext>
				</DndContext>

				{/* Inline text input */}
				{isAddingText && (
					<Input
						autoFocus
						className="h-6 w-28 text-xs px-2 border-primary/30 focus-visible:ring-1"
						value={newText}
						onChange={(e) => setNewText(e.target.value)}
						onBlur={handleAddText}
						onKeyDown={(e) => {
							if (e.key === "Enter") handleAddText();
							if (e.key === "Escape") {
								setNewText("");
								setIsAddingText(false);
							}
						}}
						placeholder="输入文本..."
					/>
				)}
			</div>

			{/* Action buttons */}
			<div className="flex items-center gap-1.5">
				{/* Add text button - first */}
				<button
					type="button"
					onClick={() => setIsAddingText(true)}
					className="inline-flex items-center gap-1 h-6 px-2 text-[11px] font-medium text-muted-foreground hover:text-foreground bg-muted/50 hover:bg-muted border border-border rounded-md transition-colors"
				>
					<Type className="h-3 w-3" />
					添加文本
				</button>

				{/* Common variables dropdown */}
				{groupedVariables.common && (
					<DropdownMenu>
						<DropdownMenuTrigger asChild>
							<button
								type="button"
								className="inline-flex items-center gap-1 h-6 px-2 text-[11px] font-medium text-primary bg-primary/10 hover:bg-primary/15 border border-primary/20 rounded-md transition-colors"
							>
								<Variable className="h-3 w-3" />
								常用变量
							</button>
						</DropdownMenuTrigger>
						<DropdownMenuContent align="start" className="w-48">
							{groupedVariables.common.map((v) => (
								<DropdownMenuItem
									key={v.key}
									onClick={() => handleAddVariable(v.key)}
									className="flex items-center justify-between gap-2 cursor-pointer"
								>
									<span className="text-xs">{v.label}</span>
									<Badge variant="secondary" className="text-[10px] font-mono px-1.5 py-0">
										{`{${v.key}}`}
									</Badge>
								</DropdownMenuItem>
							))}
						</DropdownMenuContent>
					</DropdownMenu>
				)}

				{/* Metadata variables dropdown */}
				{groupedVariables.metadata && (
					<DropdownMenu>
						<DropdownMenuTrigger asChild>
							<button
								type="button"
								className="inline-flex items-center gap-1 h-6 px-2 text-[11px] font-medium text-teal-600 dark:text-teal-400 bg-teal-500/10 hover:bg-teal-500/15 border border-teal-500/20 rounded-md transition-colors"
							>
								<Variable className="h-3 w-3" />
								元数据
							</button>
						</DropdownMenuTrigger>
						<DropdownMenuContent align="start" className="w-48">
							{groupedVariables.metadata.map((v) => (
								<DropdownMenuItem
									key={v.key}
									onClick={() => handleAddVariable(v.key)}
									className="flex items-center justify-between gap-2 cursor-pointer"
								>
									<span className="text-xs">{v.label}</span>
									<Badge variant="secondary" className="text-[10px] font-mono px-1.5 py-0">
										{`{${v.key}}`}
									</Badge>
								</DropdownMenuItem>
							))}
						</DropdownMenuContent>
					</DropdownMenu>
				)}

				{/* Preview hint */}
				{blocks.length > 0 && (
					<span className="ml-auto text-[10px] text-muted-foreground/60 select-none">
						拖拽调整顺序
					</span>
				)}
			</div>
		</div>
	);
}

export const COMMON_VARIABLES = [
	{ key: "name", label: "原文件名", category: "common" },
	{ key: "n", label: "序号", category: "common" },
	{ key: "date", label: "日期", category: "common" },
	{ key: "time", label: "时间", category: "common" },
	{ key: "datetime", label: "日期时间", category: "common" },
	{ key: "timestamp", label: "时间戳", category: "common" },
	{ key: "folderName", label: "文件夹名", category: "common" },
];

export const METADATA_VARIABLES = [
	{ key: "exif.date", label: "EXIF日期", category: "metadata" },
	{ key: "exif.camera", label: "相机型号", category: "metadata" },
	{ key: "media.artist", label: "艺术家", category: "metadata" },
	{ key: "media.title", label: "标题", category: "metadata" },
	{ key: "media.album", label: "专辑", category: "metadata" },
	{ key: "media.year", label: "年份", category: "metadata" },
	{ key: "media.track", label: "曲目号", category: "metadata" },
];
