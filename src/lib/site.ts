export const SITE_URL = normalizeSiteUrl(
	process.env.NEXT_PUBLIC_BASE_URL ?? "https://rename.tools",
);

// 站点根域名（不含 basePath），用于 metadataBase，避免与静态导出的 basePath 重复拼接
export const SITE_ORIGIN = new URL(SITE_URL).origin;

function normalizeSiteUrl(url: string): string {
	return url.replace(/\/+$/, "");
}
