import type { Metadata } from "next";
import { SITE_ORIGIN, SITE_URL } from "@/lib/site";

const LOCALE = "zh";

const METADATA = {
	title: "在线批量重命名工具",
};

type GeneratePageMetadataParams = {
	path?: string;
};

export async function generatePageMetadata({
	path = "",
}: GeneratePageMetadataParams = {}): Promise<Metadata> {
	const url = `${SITE_URL}${path}`;
	const imageUrl = `${SITE_URL}/opengraph-image`;

	return {
		title: METADATA.title,
		metadataBase: new URL(SITE_ORIGIN),
		keywords: [
			"bulk rename files",
			"bulk file renamer",
			"bulk file rename",
			"batch file renamer",
			"file renaming tool",
			"regex rename",
			"batch file rename",
			"browser file rename",
			"local file processing",
			"privacy-first rename",
			"file name editor",
		],
		alternates: {
			canonical: url,
			languages: {
				zh: url,
				"x-default": url,
			},
		},
		openGraph: {
			title: METADATA.title,
			url,
			siteName: "Rename.Tools",
			locale: LOCALE,
			type: "website",
			images: [
				{
					url: imageUrl,
					width: 1200,
					height: 630,
					alt: "Rename.Tools - Advanced Bulk File Renamer",
				},
			],
		},
		twitter: {
			card: "summary_large_image",
			title: METADATA.title,
			images: [imageUrl],
		},
	};
}
