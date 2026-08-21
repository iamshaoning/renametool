"use client";

import { toast } from "sonner";
import { useShallow } from "zustand/react/shallow";
import { FilePanel } from "@/components/rename/FilePanel";
import { PreviewPanel } from "@/components/rename/PreviewPanel";
import { RenameHeader } from "@/components/rename/RenameHeader";
import { RulePanel } from "@/components/rename/RulePanel";
import { ResizableHandle, ResizablePanel, ResizablePanelGroup } from "@/components/ui/resizable";
import { useMetadataLoader } from "@/hooks/useMetadataLoader";
import { usePresetsStore } from "@/hooks/usePresetsStore";
import { useRenameStore } from "@/hooks/useRenameStore";

export default function RenameAppPage() {
	// ── Selector-based subscriptions for granular re-renders ──
	const {
		files,
		filteredFiles,
		addFiles,
		clearFiles,
		toggleFileSelection,
		selectAll,
		sortFiles,
		sortMode,
		filter,
		addFilterCondition,
		updateFilterCondition,
		removeFilterCondition,
		setFilterLogic,
		clearFilter,
		updateFileMetadata,
		hasMetadata,
	} = useRenameStore(
		useShallow((s) => ({
			files: s.files,
			filteredFiles: s.filteredFiles,
			addFiles: s.addFiles,
			clearFiles: s.clearFiles,
			toggleFileSelection: s.toggleFileSelection,
			selectAll: s.selectAll,
			sortFiles: s.sortFiles,
			sortMode: s.sortMode,
			filter: s.filter,
			addFilterCondition: s.addFilterCondition,
			updateFilterCondition: s.updateFilterCondition,
			removeFilterCondition: s.removeFilterCondition,
			setFilterLogic: s.setFilterLogic,
			clearFilter: s.clearFilter,
			updateFileMetadata: s.updateFileMetadata,
			hasMetadata: s.hasMetadata,
		})),
	);

	const {
		rules,
		extensionScope,
		addRule,
		addRulesFromTemplate,
		updateRule,
		removeRule,
		reorderRules,
		cloneRule,
		setExtensionScope,
		clearRules,
	} = useRenameStore(
		useShallow((s) => ({
			rules: s.rules,
			extensionScope: s.extensionScope,
			addRule: s.addRule,
			addRulesFromTemplate: s.addRulesFromTemplate,
			updateRule: s.updateRule,
			removeRule: s.removeRule,
			reorderRules: s.reorderRules,
			cloneRule: s.cloneRule,
			setExtensionScope: s.setExtensionScope,
			clearRules: s.clearRules,
		})),
	);

	const {
		preview,
		isPreviewComputing,
		applyAutoFix,
		resetAutoFix,
		hasAutoFix,
		executionLog,
		executionProgress,
		isExecuting,
		needsUserActivation,
		clearExecutionLog,
		confirmUserActivation,
		execute,
		undo,
		redo,
		canUndo,
		canRedo,
	} = useRenameStore(
		useShallow((s) => ({
			preview: s.preview,
			isPreviewComputing: s.isPreviewComputing,
			applyAutoFix: s.applyAutoFix,
			resetAutoFix: s.resetAutoFix,
			hasAutoFix: s.hasAutoFix,
			executionLog: s.executionLog,
			executionProgress: s.executionProgress,
			isExecuting: s.isExecuting,
			needsUserActivation: s.needsUserActivation,
			clearExecutionLog: s.clearExecutionLog,
			confirmUserActivation: s.confirmUserActivation,
			execute: s.execute,
			undo: s.undo,
			redo: s.redo,
			canUndo: s.canUndo,
			canRedo: s.canRedo,
		})),
	);

	const savePreset = usePresetsStore((state) => state.savePreset);
	const metadataLoader = useMetadataLoader();

	return (
		<div className="flex h-screen flex-col bg-background">
			<RenameHeader />
			<ResizablePanelGroup orientation="horizontal" className="flex-1 min-h-0">
				<ResizablePanel defaultSize={20} minSize={12}>
					<FilePanel
						allFiles={files}
						filteredFiles={filteredFiles}
						onAddFiles={addFiles}
						onToggle={toggleFileSelection}
						onSelectAll={selectAll}
						onClear={clearFiles}
						onSortFiles={sortFiles}
						sortMode={sortMode}
						filterConditions={filter.conditions}
						filterLogic={filter.logic}
						onAddFilterCondition={addFilterCondition}
						onUpdateFilterCondition={updateFilterCondition}
						onRemoveFilterCondition={removeFilterCondition}
						onSetFilterLogic={setFilterLogic}
						onClearFilter={clearFilter}
						onLoadMetadata={() => metadataLoader.loadMetadata(files, updateFileMetadata)}
						metadataProgress={metadataLoader.progress}
						hasMetadata={hasMetadata}
					/>
				</ResizablePanel>
				<ResizableHandle />
				<ResizablePanel defaultSize={25} minSize={15}>
					<div className="flex h-full flex-col">
						<div className="flex-1 min-h-0">
							<RulePanel
								rules={rules}
								extensionScope={extensionScope}
								onAddRule={addRule}
								onAddRulesFromTemplate={addRulesFromTemplate}
								onUpdateRule={updateRule}
								onRemoveRule={removeRule}
								onReorderRules={reorderRules}
								onCloneRule={cloneRule}
								onSetExtensionScope={setExtensionScope}
								onClearRules={clearRules}
								onSavePreset={(name, options) => {
									savePreset(
										name,
										rules.filter((r) => r.enabled).map((r) => r.ruleConfig),
										options,
									);
									toast.success("预设已保存", {
										description: `预设「${name}」已成功保存`,
									});
								}}
								hasMetadata={hasMetadata}
							/>
						</div>
					</div>
				</ResizablePanel>
				<ResizableHandle />
				<ResizablePanel defaultSize={55} minSize={20}>
					<PreviewPanel
						preview={preview}
						isPreviewComputing={isPreviewComputing}
						applyAutoFix={applyAutoFix}
						resetAutoFix={resetAutoFix}
						hasAutoFix={hasAutoFix}
						log={executionLog}
						progress={executionProgress}
						isExecuting={isExecuting}
						needsUserActivation={needsUserActivation}
						onClearLog={clearExecutionLog}
						onConfirmActivation={confirmUserActivation}
						onExecute={execute}
						onUndo={undo}
						onRedo={redo}
						canUndo={canUndo}
						canRedo={canRedo}
					/>
				</ResizablePanel>
			</ResizablePanelGroup>
		</div>
	);
}
