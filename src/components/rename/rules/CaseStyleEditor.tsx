import { Label } from "@/components/ui/label";
import {
	Select,
	SelectContent,
	SelectGroup,
	SelectItem,
	SelectLabel,
	SelectTrigger,
	SelectValue,
} from "@/components/ui/select";
import type { CaseStyleConfig } from "@/lib/rename/types";

interface CaseStyleEditorProps {
	config: CaseStyleConfig;
	onChange: (c: Partial<CaseStyleConfig>) => void;
}

export function CaseStyleEditor({ config, onChange }: CaseStyleEditorProps) {
	const isDeveloperMode = ["camelCase", "PascalCase", "kebab-case", "snake_case"].includes(
		config.mode,
	);

	return (
		<div className="space-y-1.5">
			<div className="space-y-0.5">
				<Label className="text-[11px] text-muted-foreground">模式</Label>
				<Select
					value={config.mode}
					onValueChange={(v) => onChange({ mode: v as CaseStyleConfig["mode"] })}
				>
					<SelectTrigger className="h-7 text-xs">
						<SelectValue />
					</SelectTrigger>
					<SelectContent>
						<SelectItem value="none">—</SelectItem>
						<SelectGroup>
							<SelectLabel>通用</SelectLabel>
							<SelectItem value="uppercase">全大写</SelectItem>
							<SelectItem value="lowercase">全小写</SelectItem>
							<SelectItem value="titlecase">首字母大写</SelectItem>
							<SelectItem value="sentencecase">句首大写</SelectItem>
						</SelectGroup>
						<SelectGroup>
							<SelectLabel>开发者</SelectLabel>
							<SelectItem value="camelCase">驼峰命名</SelectItem>
							<SelectItem value="PascalCase">帕斯卡命名</SelectItem>
							<SelectItem value="kebab-case">短横线命名</SelectItem>
							<SelectItem value="snake_case">下划线命名</SelectItem>
						</SelectGroup>
					</SelectContent>
				</Select>
			</div>
			{!isDeveloperMode && (
				<div className="space-y-0.5">
					<Label className="text-[11px] text-muted-foreground">样式转换</Label>
					<Select
						value={config.style}
						onValueChange={(v) => onChange({ style: v as CaseStyleConfig["style"] })}
					>
						<SelectTrigger className="h-7 text-xs">
							<SelectValue />
						</SelectTrigger>
						<SelectContent>
							<SelectItem value="none">无</SelectItem>
							<SelectItem value="spaceToDash">空格 → 横线</SelectItem>
							<SelectItem value="spaceToUnderscore">空格 → 下划线</SelectItem>
							<SelectItem value="dashToSpace">横线 → 空格</SelectItem>
							<SelectItem value="underscoreToSpace">下划线 → 空格</SelectItem>
						</SelectContent>
					</Select>
				</div>
			)}
		</div>
	);
}
