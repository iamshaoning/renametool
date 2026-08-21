"use client";

import { Moon, Settings, Sun } from "lucide-react";
import Image from "next/image";
import { useTheme } from "next-themes";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import {
	Dialog,
	DialogContent,
	DialogDescription,
	DialogHeader,
	DialogTitle,
} from "@/components/ui/dialog";
import {
	DropdownMenu,
	DropdownMenuContent,
	DropdownMenuItem,
	DropdownMenuSeparator,
	DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";

export function RenameHeader() {
	const { theme, setTheme } = useTheme();
	const [settingsOpen, setSettingsOpen] = useState(false);

	return (
		<>
			<header className="flex h-12 items-center justify-between border-b border-slate-700/50 bg-slate-900/95 px-4 text-slate-100 backdrop-blur-sm dark:bg-slate-950/95">
				{/* Left: Logo */}
				<div className="flex items-center gap-2.5">
					<Image
						src={`${process.env.NEXT_PUBLIC_BASE_PATH || ""}/logo.svg`}
						alt="批量重命名"
						width={28}
						height={28}
						className="h-7 w-7 rounded-lg"
					/>
					<h1 className="text-base font-semibold tracking-tight text-white">批量重命名</h1>
				</div>

				{/* Center: Tauri drag region */}
				<div className="flex-1" data-tauri-drag-region />

				{/* Right: Controls */}
				<div className="flex items-center gap-0.5">
					{/* Theme Toggle */}
					<Button
						variant="ghost"
						size="icon"
						className="h-8 w-8 text-slate-300 hover:bg-slate-800 hover:text-slate-100"
						onClick={() => setTheme(theme === "dark" ? "light" : "dark")}
						aria-label="切换主题"
					>
						<Sun className="h-4 w-4 rotate-0 scale-100 transition-transform dark:-rotate-90 dark:scale-0" />
						<Moon className="absolute h-4 w-4 rotate-90 scale-0 transition-transform dark:rotate-0 dark:scale-100" />
					</Button>

					{/* Settings */}
					<DropdownMenu>
						<DropdownMenuTrigger asChild>
							<Button
								variant="ghost"
								size="icon"
								className="h-8 w-8 text-slate-300 hover:bg-slate-800 hover:text-slate-100"
								aria-label="设置"
							>
								<Settings className="h-4 w-4" />
							</Button>
						</DropdownMenuTrigger>
						<DropdownMenuContent align="end" className="w-48">
						<DropdownMenuItem onClick={() => setSettingsOpen(true)}>
							偏好设置
						</DropdownMenuItem>
						<DropdownMenuSeparator />
					</DropdownMenuContent>
				</DropdownMenu>
			</div>
		</header>

			{/* Settings Dialog (placeholder for future preferences) */}
			<Dialog open={settingsOpen} onOpenChange={setSettingsOpen}>
				<DialogContent className="sm:max-w-md">
					<DialogHeader>
						<DialogTitle className="flex items-center gap-2">
							<Settings className="h-5 w-5 text-muted-foreground" />
							偏好设置
						</DialogTitle>
						<DialogDescription>配置应用的全局参数和行为。</DialogDescription>
					</DialogHeader>

					<div className="space-y-6 py-4">
						<div className="text-sm text-muted-foreground">更多设置选项即将推出...</div>
					</div>
				</DialogContent>
			</Dialog>
		</>
	);
}
