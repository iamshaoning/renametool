import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { SITE_ORIGIN, SITE_URL } from "@/lib/site";

const LOCALE = "zh";

type GeneratePageMetadataParams = {
	path?: string;
	namespace?: string;
};

export async function generatePageMetadata({
	path = "",
	namespace = "metadata",
}: GeneratePageMetadataParams = {}): Promise<Metadata> {
	const t = await getTranslations({ locale: LOCALE, namespace });

	const url = `${SITE_URL}${path}`;
	const imageUrl = `${SITE_URL}/opengraph-image`;

	return {
		title: t("title"),
		description: t("description"),
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
			title: t("title"),
			description: t("description"),
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
			title: t("title"),
			description: t("description"),
			images: [imageUrl],
		},
	};
}
