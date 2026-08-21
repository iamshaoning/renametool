"use client";

import { Play, Trash2 } from "lucide-react";
import { useState } from "react";
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
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import type { PreviewResult } from "@/lib/rename/types";

interface Props {
	preview: PreviewResult[];
	isExecuting: boolean;
	onExecute: () => void;
	onClearRules: () => void;
}

export function ExecuteBar({ preview, isExecuting, onExecute, onClearRules }: Props) {
	const [warningChecked, setWarningChecked] = useState(false);

	const affectedCount = preview.filter((r) => r.hasChange).length;
	const hasConflicts = preview.some((r) => r.conflict);

	return (
		<div className="flex items-center gap-2 border-t bg-card px-4 py-2.5 shadow-[0_-2px_10px_hsl(var(--border)/0.5)]">
			<div className="ml-auto flex items-center gap-2">
				<Button variant="outline" size="sm" className="gap-1 text-xs" onClick={onClearRules}>
					<Trash2 className="h-3.5 w-3.5" /> 清除规则
				</Button>

				<AlertDialog>
					<AlertDialogTrigger asChild>
						<button
							type="button"
							className="inline-flex items-center gap-1.5 rounded-md brand-gradient px-4 py-2 text-sm font-medium text-white shadow-sm transition-all hover:shadow-lg disabled:opacity-50 disabled:pointer-events-none"
							disabled={affectedCount === 0 || hasConflicts || isExecuting}
						>
							<Play className="h-3.5 w-3.5" />
							执行重命名 ({affectedCount})
						</button>
					</AlertDialogTrigger>
					<AlertDialogContent>
						<AlertDialogHeader>
							<AlertDialogTitle>确认重命名</AlertDialogTitle>
							<AlertDialogDescription>即将重命名 {affectedCount} 个文件。</AlertDialogDescription>
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
	);
}
