"use client";

import { create } from "zustand";
import { persist } from "zustand/middleware";
import type {
	PresetCategory,
	PresetPinned,
	PresetSortMode,
	RuleConfig,
	UserPreset,
} from "@/lib/rename/types";

let nextPresetId = 1;
function generatePresetId() {
	return `preset_${Date.now()}_${nextPresetId++}`;
}

interface PresetsState {
	presets: UserPreset[];
	pinned: PresetPinned[];
	sortMode: PresetSortMode;
	searchQuery: string;
	categoryFilter: PresetCategory | "all";
	setSortMode: (mode: PresetSortMode) => void;
	setSearchQuery: (query: string) => void;
	setCategoryFilter: (filter: PresetCategory | "all") => void;
	savePreset: (
		name: string,
		rules: RuleConfig[],
		options?: {
			description?: string;
			tags?: string[];
			category?: PresetCategory;
		},
	) => string;
	updatePreset: (id: string, updates: Partial<Omit<UserPreset, "id" | "createdAt">>) => void;
	deletePreset: (id: string) => void;
	incrementUsage: (id: string) => void;
	togglePin: (type: "system" | "user", id: string) => void;
	isPinned: (type: "system" | "user", id: string) => boolean;
}

export const usePresetsStore = create<PresetsState>()(
	persist(
		(set, get) => ({
			presets: [],
			pinned: [],
			sortMode: "recent",
			searchQuery: "",
			categoryFilter: "all",

			setSortMode: (mode) => set({ sortMode: mode }),
			setSearchQuery: (query) => set({ searchQuery: query }),
			setCategoryFilter: (filter) => set({ categoryFilter: filter }),

			savePreset: (name, rules, options) => {
				const newPreset: UserPreset = {
					id: generatePresetId(),
					name,
					description: options?.description,
					tags: options?.tags,
					category: options?.category,
					rules,
					createdAt: Date.now(),
					lastUsedAt: Date.now(),
					usageCount: 0,
				};

				set((state) => ({
					presets: [...state.presets, newPreset],
				}));

				return newPreset.id;
			},

			updatePreset: (id, updates) => {
				set((state) => ({
					presets: state.presets.map((p) => (p.id === id ? { ...p, ...updates } : p)),
				}));
			},

			deletePreset: (id) => {
				set((state) => ({
					presets: state.presets.filter((p) => p.id !== id),
					pinned: state.pinned.filter((p) => !(p.type === "user" && p.id === id)),
				}));
			},

			incrementUsage: (id) => {
				set((state) => ({
					presets: state.presets.map((p) =>
						p.id === id ? { ...p, lastUsedAt: Date.now(), usageCount: p.usageCount + 1 } : p,
					),
				}));
			},

			togglePin: (type, id) => {
				set((state) => {
					const existingIndex = state.pinned.findIndex((p) => p.type === type && p.id === id);

					if (existingIndex >= 0) {
						return {
							pinned: state.pinned.filter((_, i) => i !== existingIndex),
						};
					} else {
						return {
							pinned: [...state.pinned, { type, id, pinnedAt: Date.now() }],
						};
					}
				});
			},

			isPinned: (type, id) => {
				return get().pinned.some((p) => p.type === type && p.id === id);
			},
		}),
		{
			name: "renametools:presets-storage",
			partialize: (state) => ({
				presets: state.presets,
				pinned: state.pinned,
			}),
		},
	),
);

// Selector hook for filtered and sorted presets
export function useFilteredPresets() {
	const presets = usePresetsStore((state) => state.presets);
	const searchQuery = usePresetsStore((state) => state.searchQuery);
	const categoryFilter = usePresetsStore((state) => state.categoryFilter);
	const sortMode = usePresetsStore((state) => state.sortMode);

	let result = [...presets];

	if (searchQuery) {
		const query = searchQuery.toLowerCase();
		result = result.filter(
			(p) =>
				p.name.toLowerCase().includes(query) ||
				p.description?.toLowerCase().includes(query) ||
				p.tags?.some((t) => t.toLowerCase().includes(query)),
		);
	}

	if (categoryFilter !== "all") {
		result = result.filter((p) => p.category === categoryFilter);
	}

	switch (sortMode) {
		case "recent":
			result.sort((a, b) => b.lastUsedAt - a.lastUsedAt);
			break;
		case "frequent":
			result.sort((a, b) => b.usageCount - a.usageCount);
			break;
		case "name":
			result.sort((a, b) => a.name.localeCompare(b.name));
			break;
		case "created":
			result.sort((a, b) => b.createdAt - a.createdAt);
			break;
	}

	return result;
}
