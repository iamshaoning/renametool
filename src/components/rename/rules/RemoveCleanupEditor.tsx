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
import type { RemoveCleanupConfig } from "@/lib/rename/types";

interface RemoveCleanupEditorProps {
	config: RemoveCleanupConfig;
	onChange: (c: Partial<RemoveCleanupConfig>) => void;
}

export function RemoveCleanupEditor({ config, onChange }: RemoveCleanupEditorProps) {
	return (
		<div className="space-y-1.5">
			<div className="space-y-0.5">
				<Label className="text-[11px] text-muted-foreground">模式</Label>
				<Select
					value={config.mode}
					onValueChange={(v) => onChange({ mode: v as RemoveCleanupConfig["mode"] })}
				>
					<SelectTrigger className="h-7 text-xs">
						<SelectValue />
					</SelectTrigger>
					<SelectContent>
						<SelectItem value="chars">按字符数</SelectItem>
						<SelectItem value="range">按范围</SelectItem>
						<SelectItem value="cleanup">快捷清洗</SelectItem>
					</SelectContent>
				</Select>
			</div>

			{config.mode === "chars" && (
				<div className="space-y-1.5">
					<div className="space-y-0.5">
						<Label className="text-[11px] text-muted-foreground">方向</Label>
						<Select
							value={config.direction}
							onValueChange={(v) => onChange({ direction: v as RemoveCleanupConfig["direction"] })}
						>
							<SelectTrigger className="h-7 text-xs">
								<SelectValue />
							</SelectTrigger>
							<SelectContent>
								<SelectItem value="start">从头部</SelectItem>
								<SelectItem value="end">从尾部</SelectItem>
							</SelectContent>
						</Select>
					</div>
					<div className="space-y-0.5">
						<Label className="text-[11px] text-muted-foreground">字符数</Label>
						<Input
							className="h-7 text-xs"
							type="number"
							min="0"
							value={config.count}
							onChange={(e) => onChange({ count: +e.target.value })}
						/>
					</div>
				</div>
			)}

			{config.mode === "range" && (
				<div className="grid grid-cols-2 gap-1.5">
					<div className="space-y-0.5">
						<Label className="text-[11px] text-muted-foreground">起始位置</Label>
						<Input
							className="h-7 text-xs"
							type="number"
							min="0"
							value={config.rangeStart}
							onChange={(e) => onChange({ rangeStart: +e.target.value })}
						/>
					</div>
					<div className="space-y-0.5">
						<Label className="text-[11px] text-muted-foreground">结束位置</Label>
						<Input
							className="h-7 text-xs"
							type="number"
							min="0"
							value={config.rangeEnd}
							onChange={(e) => onChange({ rangeEnd: +e.target.value })}
						/>
					</div>
				</div>
			)}

			{config.mode === "cleanup" && (
				<div className="space-y-1.5">
					<Label className="text-[11px] text-muted-foreground">快捷清洗</Label>
					<div className="grid grid-cols-2 gap-x-3 gap-y-1.5">
						<div className="flex items-center gap-1.5">
							<Checkbox
								checked={config.removeDigits}
								onCheckedChange={(v) => onChange({ removeDigits: !!v })}
								className="h-3.5 w-3.5"
							/>
							<Label className="text-[11px] text-muted-foreground">数字</Label>
						</div>
						<div className="flex items-center gap-1.5">
							<Checkbox
								checked={config.removeSymbols}
								onCheckedChange={(v) => onChange({ removeSymbols: !!v })}
								className="h-3.5 w-3.5"
							/>
							<Label className="text-[11px] text-muted-foreground">符号</Label>
						</div>
						<div className="flex items-center gap-1.5">
							<Checkbox
								checked={config.removeSpaces}
								onCheckedChange={(v) => onChange({ removeSpaces: !!v })}
								className="h-3.5 w-3.5"
							/>
							<Label className="text-[11px] text-muted-foreground">空格</Label>
						</div>
						<div className="flex items-center gap-1.5">
							<Checkbox
								checked={config.removeChinese}
								onCheckedChange={(v) => onChange({ removeChinese: !!v })}
								className="h-3.5 w-3.5"
							/>
							<Label className="text-[11px] text-muted-foreground">中文</Label>
						</div>
						<div className="flex items-center gap-1.5">
							<Checkbox
								checked={config.removeEnglish}
								onCheckedChange={(v) => onChange({ removeEnglish: !!v })}
								className="h-3.5 w-3.5"
							/>
							<Label className="text-[11px] text-muted-foreground">英文</Label>
						</div>
					</div>
				</div>
			)}
		</div>
	);
}
