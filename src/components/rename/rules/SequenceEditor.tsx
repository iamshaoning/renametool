import {
	COMMON_VARIABLES,
	METADATA_VARIABLES,
	TemplateEditor,
} from "@/components/rename/TemplateEditor";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
	Select,
	SelectContent,
	SelectItem,
	SelectTrigger,
	SelectValue,
} from "@/components/ui/select";
import type { SequenceConfig } from "@/lib/rename/types";

interface SequenceEditorProps {
	config: SequenceConfig;
	onChange: (c: Partial<SequenceConfig>) => void;
	hasMetadata?: boolean;
}

export function SequenceEditor({ config, onChange, hasMetadata }: SequenceEditorProps) {
	const variables = hasMetadata ? [...COMMON_VARIABLES, ...METADATA_VARIABLES] : COMMON_VARIABLES;
	return (
		<div className="space-y-1.5">
			{/* Sequence type */}
			<div className="space-y-0.5">
				<Label className="text-[11px] text-muted-foreground">序列类型</Label>
				<Select
					value={config.seqType || "numeric"}
					onValueChange={(v) => onChange({ seqType: v as SequenceConfig["seqType"] })}
				>
					<SelectTrigger className="h-7 text-xs">
						<SelectValue />
					</SelectTrigger>
					<SelectContent>
						<SelectItem value="numeric">数字 (1, 2, 3...)</SelectItem>
						<SelectItem value="alpha">字母 (A, B, C...)</SelectItem>
						<SelectItem value="roman">罗马数字 (I, II, III...)</SelectItem>
					</SelectContent>
				</Select>
			</div>
			{/* Start / Step / Padding */}
			<div className="grid grid-cols-3 gap-x-1.5">
				<div className="space-y-0.5">
					<Label className="text-[11px] text-muted-foreground">起始值</Label>
					<Input
						className="h-7 text-xs"
						type="number"
						value={config.start}
						onChange={(e) => onChange({ start: +e.target.value })}
					/>
				</div>
				<div className="space-y-0.5">
					<Label className="text-[11px] text-muted-foreground">步长</Label>
					<Input
						className="h-7 text-xs"
						type="number"
						value={config.step}
						onChange={(e) => onChange({ step: +e.target.value })}
					/>
				</div>
				<div className="space-y-0.5">
					<Label className="text-[11px] text-muted-foreground">补零位数</Label>
					<Input
						className="h-7 text-xs"
						type="number"
						value={config.padding}
						onChange={(e) => onChange({ padding: +e.target.value })}
						disabled={(config.seqType || "numeric") !== "numeric"}
					/>
				</div>
			</div>
			{/* Position (only when no template) */}
			{!config.template && (
				<div className="space-y-0.5">
					<Label className="text-[11px] text-muted-foreground">插入位置</Label>
					<Select
						value={config.position}
						onValueChange={(v) => onChange({ position: v as SequenceConfig["position"] })}
					>
						<SelectTrigger className="h-7 text-xs">
							<SelectValue />
						</SelectTrigger>
						<SelectContent>
							<SelectItem value="start">开头</SelectItem>
							<SelectItem value="end">末尾</SelectItem>
							<SelectItem value="replaceAll">替换整个文件名</SelectItem>
						</SelectContent>
					</Select>
				</div>
			)}
			{/* Template */}
			<div className="space-y-0.5">
				<Label className="text-[11px] text-muted-foreground">格式模板（可选）</Label>
				<TemplateEditor
					value={config.template || ""}
					onChange={(template) => onChange({ template })}
					variables={variables}
					placeholder="例如 Photo_'{n}'_'{date}'"
				/>
			</div>

			{/* Scope */}
			<div className="space-y-0.5">
				<Label className="text-[11px] text-muted-foreground">编号作用域</Label>
				<Select
					value={config.scope || "global"}
					onValueChange={(v) => onChange({ scope: v as SequenceConfig["scope"] })}
				>
					<SelectTrigger className="h-7 text-xs">
						<SelectValue />
					</SelectTrigger>
					<SelectContent>
						<SelectItem value="global">全局（跨所有文件）</SelectItem>
						<SelectItem value="perFolder">按文件夹分组</SelectItem>
						<SelectItem value="perExtension">按扩展名分组</SelectItem>
						<SelectItem value="perCategory">按文件类别分组</SelectItem>
					</SelectContent>
				</Select>
			</div>

			{/* Sort before numbering */}
			<div className="space-y-1">
				<div className="flex items-center gap-1.5">
					<Checkbox
						checked={config.sortBeforeNumbering ?? false}
						onCheckedChange={(v) => onChange({ sortBeforeNumbering: !!v })}
						className="h-3.5 w-3.5"
					/>
					<Label className="text-[11px] text-muted-foreground">编号前先排序</Label>
				</div>
				{config.sortBeforeNumbering && (
					<div className="grid grid-cols-2 gap-x-1.5">
						<div className="space-y-0.5">
							<Label className="text-[11px] text-muted-foreground">排序依据</Label>
							<Select
								value={config.sortBy || "name"}
								onValueChange={(v) => onChange({ sortBy: v as SequenceConfig["sortBy"] })}
							>
								<SelectTrigger className="h-7 text-xs">
									<SelectValue />
								</SelectTrigger>
								<SelectContent>
									<SelectItem value="name">文件名</SelectItem>
									<SelectItem value="size">文件大小</SelectItem>
									<SelectItem value="modified">修改时间</SelectItem>
									<SelectItem value="extension">扩展名</SelectItem>
								</SelectContent>
							</Select>
						</div>
						<div className="space-y-0.5">
							<Label className="text-[11px] text-muted-foreground">排序方向</Label>
							<Select
								value={config.sortOrder || "asc"}
								onValueChange={(v) => onChange({ sortOrder: v as SequenceConfig["sortOrder"] })}
							>
								<SelectTrigger className="h-7 text-xs">
									<SelectValue />
								</SelectTrigger>
								<SelectContent>
									<SelectItem value="asc">升序</SelectItem>
									<SelectItem value="desc">降序</SelectItem>
								</SelectContent>
							</Select>
						</div>
						{(config.sortBy || "name") === "name" && (
							<div className="col-span-2 flex items-center gap-1.5 mt-0.5">
								<Checkbox
									checked={config.naturalSort ?? true}
									onCheckedChange={(v) => onChange({ naturalSort: !!v })}
									className="h-3.5 w-3.5"
								/>
								<Label className="text-[11px] text-muted-foreground">
									自然排序 (1, 2, 10 而非 1, 10, 2)
								</Label>
							</div>
						)}
					</div>
				)}
			</div>

			{/* Preserve original numbers */}
			<div className="space-y-1">
				<div className="flex items-center gap-1.5">
					<Checkbox
						checked={config.preserveOriginal ?? false}
						onCheckedChange={(v) => onChange({ preserveOriginal: !!v })}
						className="h-3.5 w-3.5"
					/>
					<Label className="text-[11px] text-muted-foreground">保留原有序号</Label>
				</div>
				{config.preserveOriginal && (
					<div className="space-y-0.5">
						<Label className="text-[11px] text-muted-foreground">提取模式（正则）</Label>
						<Input
							className="h-7 text-xs font-mono"
							value={config.preservePattern || "(\\d+)"}
							onChange={(e) => onChange({ preservePattern: e.target.value })}
							placeholder="(\\d+)"
						/>
						<p className="text-[10px] text-muted-foreground/70 leading-tight">
							使用捕获组提取数字，例如 (\d+)
						</p>
					</div>
				)}
			</div>

			{/* Hierarchical numbering */}
			{(config.scope || "global") !== "global" && (
				<div className="space-y-1">
					<div className="flex items-center gap-1.5">
						<Checkbox
							checked={config.hierarchical ?? false}
							onCheckedChange={(v) => onChange({ hierarchical: !!v })}
							className="h-3.5 w-3.5"
						/>
						<Label className="text-[11px] text-muted-foreground">层级编号（如 1.1, 1.2）</Label>
					</div>
					{config.hierarchical && (
						<div className="space-y-0.5">
							<Label className="text-[11px] text-muted-foreground">分隔符</Label>
							<Select
								value={config.hierarchySeparator || "."}
								onValueChange={(v) =>
									onChange({ hierarchySeparator: v as SequenceConfig["hierarchySeparator"] })
								}
							>
								<SelectTrigger className="h-7 text-xs">
									<SelectValue />
								</SelectTrigger>
								<SelectContent>
									<SelectItem value=".">. (dot)</SelectItem>
									<SelectItem value="-">- (dash)</SelectItem>
									<SelectItem value="_">_ (underscore)</SelectItem>
								</SelectContent>
							</Select>
						</div>
					)}
				</div>
			)}
		</div>
	);
}
