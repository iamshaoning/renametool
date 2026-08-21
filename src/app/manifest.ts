import type { MetadataRoute } from "next";

export const dynamic = "force-static";

export default function manifest(): MetadataRoute.Manifest {
	return {
		name: "Rename.Tools - Bulk File Renamer",
		short_name: "Rename.Tools",
		description:
			"Free browser-based bulk file renaming tool with regex, sequences, and rule chains. 100% local processing.",
		start_url: "/",
		display: "standalone",
		orientation: "any",
		background_color: "#ffffff",
		theme_color: "#667eea",
		categories: ["utilities", "productivity"],
		icons: [
			{
				src: "/icon-192.png",
				sizes: "192x192",
				type: "image/png",
			},
			{
				src: "/icon-512.png",
				sizes: "512x512",
				type: "image/png",
			},
			{
				src: "/logo.svg",
				sizes: "any",
				type: "image/svg+xml",
				purpose: "maskable",
			},
		],
	};
}
