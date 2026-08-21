import type { NextConfig } from "next";

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

export default nextConfig;
