import type { Metadata } from "next";
import { SITE_ORIGIN, SITE_URL } from "@/lib/site";

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

	return {
		title: METADATA.title,
		metadataBase: new URL(SITE_ORIGIN),
		keywords: ["批量重命名", "批量改名", "文件重命名", "正则重命名", "批量重命名工具"],
		alternates: {
			canonical: url,
		},
		openGraph: {
			title: METADATA.title,
			url,
			siteName: "批量重命名",
			locale: "zh_CN",
			type: "website",
		},
	};
}
