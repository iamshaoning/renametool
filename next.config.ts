import type { NextConfig } from "next";
import createNextIntlPlugin from "next-intl/plugin";

const withNextIntl = createNextIntlPlugin("./src/i18n/request.ts");

const nextConfig: NextConfig = {
	reactCompiler: true,
	output: "export",
	basePath: process.env.NEXT_PUBLIC_BASE_PATH || "",
	images: {
		unoptimized: true,
	},
	turbopack: {
		root: process.cwd(),
	},
};

export default withNextIntl(nextConfig);
