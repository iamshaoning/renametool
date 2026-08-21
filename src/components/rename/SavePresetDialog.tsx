"use client";

import { Save } from "lucide-react";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import {
	Dialog,
	DialogContent,
	DialogDescription,
	DialogFooter,
	DialogHeader,
	DialogTitle,
	DialogTrigger,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
	Select,
	SelectContent,
	SelectItem,
	SelectTrigger,
	SelectValue,
} from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import type { PresetCategory, RuleConfig } from "@/lib/rename/types";

interface Props {
	rules: RuleConfig[];
	onSave: (
		name: string,
		options: {
			description?: string;
			tags?: string[];
			category?: PresetCategory;
		},
	) => void;
	trigger: React.ReactNode;
}

export function SavePresetDialog({ rules, onSave, trigger }: Props) {
	const [open, setOpen] = useState(false);
	const [name, setName] = useState("");
	const [description, setDescription] = useState("");
	const [category, setCategory] = useState<PresetCategory | "">("");
	const [tagsInput, setTagsInput] = useState("");

	const handleSave = () => {
		if (!name.trim()) return;

		const tags = tagsInput
			.split(",")
			.map((t) => t.trim())
			.filter(Boolean);

		onSave(name.trim(), {
			description: description.trim() || undefined,
			tags: tags.length > 0 ? tags : undefined,
			category: category || undefined,
		});

		setName("");
		setDescription("");
		setCategory("");
		setTagsInput("");
		setOpen(false);
	};

	return (
		<Dialog open={open} onOpenChange={setOpen}>
			<DialogTrigger asChild>{trigger}</DialogTrigger>
			<DialogContent className="sm:max-w-md">
				<DialogHeader>
					<DialogTitle className="flex items-center gap-2">
						<Save className="h-4 w-4" />
						保存为预设
					</DialogTitle>
					<DialogDescription>将当前 {rules.length} 条规则保存为可重用预设</DialogDescription>
				</DialogHeader>

				<div className="space-y-4 py-4">
					<div className="space-y-2">
						<Label htmlFor="preset-name">预设名称</Label>
						<Input
							id="preset-name"
							placeholder="例如：照片清理工作流"
							value={name}
							onChange={(e) => setName(e.target.value)}
							onKeyDown={(e) => {
								if (e.key === "Enter" && name.trim()) {
									handleSave();
								}
							}}
						/>
					</div>

					<div className="space-y-2">
						<Label htmlFor="preset-description">
							描述 (可选)
						</Label>
						<Textarea
							id="preset-description"
							placeholder="这个预设做什么？"
							value={description}
							onChange={(e) => setDescription(e.target.value)}
							rows={2}
						/>
					</div>

					<div className="space-y-2">
						<Label htmlFor="preset-category">
							分类 (可选)
						</Label>
						<Select value={category} onValueChange={(v) => setCategory(v as PresetCategory)}>
							<SelectTrigger id="preset-category">
								<SelectValue placeholder="选择分类" />
							</SelectTrigger>
							<SelectContent>
								<SelectItem value="photo">照片</SelectItem>
								<SelectItem value="document">文档</SelectItem>
								<SelectItem value="code">代码</SelectItem>
								<SelectItem value="video">视频</SelectItem>
								<SelectItem value="music">音乐</SelectItem>
								<SelectItem value="general">通用</SelectItem>
							</SelectContent>
						</Select>
					</div>

					<div className="space-y-2">
						<Label htmlFor="preset-tags">
							标签 (可选)
						</Label>
						<Input
							id="preset-tags"
							placeholder="照片, 清理, 批量"
							value={tagsInput}
							onChange={(e) => setTagsInput(e.target.value)}
						/>
						<p className="text-xs text-muted-foreground">用逗号分隔标签</p>
					</div>
				</div>

				<DialogFooter>
					<Button variant="outline" onClick={() => setOpen(false)}>
						取消
					</Button>
					<Button onClick={handleSave} disabled={!name.trim()}>
						<Save className="h-4 w-4 mr-1.5" />
						保存
					</Button>
				</DialogFooter>
			</DialogContent>
		</Dialog>
	);
}
