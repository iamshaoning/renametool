"use client";

import { Plus, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import {
	Select,
	SelectContent,
	SelectItem,
	SelectTrigger,
	SelectValue,
} from "@/components/ui/select";
import type { FilterCondition, FilterField, FilterOperator } from "@/lib/rename/types";

interface Props {
	conditions: FilterCondition[];
	logic: "AND" | "OR";
	onAddCondition: () => void;
	onUpdateCondition: (id: string, updates: Partial<FilterCondition>) => void;
	onRemoveCondition: (id: string) => void;
	onSetLogic: (logic: "AND" | "OR") => void;
	onClearAll: () => void;
}

export function FilterPanel({
	conditions,
	logic,
	onAddCondition,
	onUpdateCondition,
	onRemoveCondition,
	onSetLogic,
	onClearAll,
}: Props) {
	const fieldOptions: { value: FilterField; label: string }[] = [
		{ value: "name", label: "文件名" },
		{ value: "extension", label: "扩展名" },
		{ value: "size", label: "文件大小" },
		{ value: "modified", label: "修改时间" },
	];

	const textOperators: { value: FilterOperator; label: string }[] = [
		{ value: "contains", label: "包含" },
		{ value: "notContains", label: "不包含" },
		{ value: "equals", label: "等于" },
		{ value: "notEquals", label: "不等于" },
		{ value: "startsWith", label: "开头是" },
		{ value: "endsWith", label: "结尾是" },
		{ value: "regex", label: "正则匹配" },
	];

	const numberOperators: { value: FilterOperator; label: string }[] = [
		{ value: "greaterThan", label: "大于" },
		{ value: "lessThan", label: "小于" },
		{ value: "equals", label: "等于" },
	];

	const getOperatorOptions = (field: FilterField) => {
		if (field === "size" || field === "modified") {
			return numberOperators;
		}
		return textOperators;
	};

	const getPlaceholder = (field: FilterField) => {
		switch (field) {
			case "name":
				return "输入文件名...";
			case "extension":
				return "如 jpg, png, pdf";
			case "size":
				return "如 100KB, 5MB, 1GB";
			case "modified":
				return "选择日期";
			default:
				return "输入值...";
		}
	};

	return (
		<div className="space-y-2">
			<div className="flex items-center justify-between mb-3">
				<h3 className="text-sm font-medium">文件过滤</h3>
				<div className="flex items-center gap-1">
					{conditions.length > 0 && (
						<Button variant="ghost" size="sm" onClick={onClearAll} className="h-7 px-2 text-xs">
							清除全部
						</Button>
					)}
					<Button
						variant="outline"
						size="sm"
						onClick={onAddCondition}
						className="h-7 px-2 gap-1 text-xs"
					>
						<Plus className="h-3.5 w-3.5" />
						添加条件
					</Button>
				</div>
			</div>

			{conditions.length === 0 && (
				<p className="text-xs text-muted-foreground py-2">暂无筛选条件，点击添加</p>
			)}

			<div className="space-y-2">
				{conditions.map((condition, index) => (
					<div key={condition.id} className="flex items-center gap-2">
						<div className="flex items-center gap-2 flex-1 min-w-0">
							{index === 0 ? (
								<span className="text-xs text-muted-foreground w-15 shrink-0 text-center">
									条件
								</span>
							) : (
								<Select value={logic} onValueChange={(v) => onSetLogic(v as "AND" | "OR")}>
									<SelectTrigger size="sm" className="h-8 w-15 shrink-0 text-xs">
										<SelectValue />
									</SelectTrigger>
									<SelectContent>
										<SelectItem value="AND">且</SelectItem>
										<SelectItem value="OR">或</SelectItem>
									</SelectContent>
								</Select>
							)}

							<Select
								value={condition.field}
								onValueChange={(v) => {
									const newField = v as FilterField;
									const operators = getOperatorOptions(newField);
									const validOperator = operators.find((op) => op.value === condition.operator)
										? condition.operator
										: operators[0].value;
									onUpdateCondition(condition.id, { field: newField, operator: validOperator });
								}}
							>
								<SelectTrigger size="sm" className="h-8 w-25 shrink-0 text-xs">
									<SelectValue />
								</SelectTrigger>
								<SelectContent>
									{fieldOptions.map((option) => (
										<SelectItem key={option.value} value={option.value}>
											{option.label}
										</SelectItem>
									))}
								</SelectContent>
							</Select>

							<Select
								value={condition.operator}
								onValueChange={(v) =>
									onUpdateCondition(condition.id, { operator: v as FilterOperator })
								}
							>
								<SelectTrigger size="sm" className="h-8 w-25 shrink-0 text-xs">
									<SelectValue />
								</SelectTrigger>
								<SelectContent>
									{getOperatorOptions(condition.field).map((option) => (
										<SelectItem key={option.value} value={option.value}>
											{option.label}
										</SelectItem>
									))}
								</SelectContent>
							</Select>

							<Input
								value={condition.value}
								onChange={(e) => onUpdateCondition(condition.id, { value: e.target.value })}
								placeholder={getPlaceholder(condition.field)}
								type={condition.field === "modified" ? "date" : "text"}
								className="h-8 text-xs flex-1 min-w-30"
							/>

							{(condition.field === "name" || condition.field === "extension") && (
								<div className="flex items-center gap-1 px-2 border-l">
									<Checkbox
										checked={condition.caseSensitive}
										onCheckedChange={(checked) =>
											onUpdateCondition(condition.id, { caseSensitive: !!checked })
										}
										className="h-3.5 w-3.5"
									/>
									<span className="text-xs text-muted-foreground whitespace-nowrap">
										区分大小写
									</span>
								</div>
							)}
						</div>

						<Button
							variant="ghost"
							size="sm"
							onClick={() => onRemoveCondition(condition.id)}
							className="h-8 w-8 p-0 shrink-0"
						>
							<X className="h-4 w-4" />
						</Button>
					</div>
				))}
			</div>
		</div>
	);
}
