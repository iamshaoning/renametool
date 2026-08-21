import { Geist, Geist_Mono, Noto_Sans_SC } from "next/font/google";
import type { ReactNode } from "react";
import { GoogleAnalytics } from "@/components/GoogleAnalytics";
import { ThemeProvider } from "@/components/ThemeProvider";
import { UmamiAnalytics } from "@/components/UmamiAnalytics";
import { Toaster } from "@/components/ui/sonner";
import { generatePageMetadata } from "@/lib/metadata";
import "./globals.css";

const geistSans = Geist({
	variable: "--font-geist-sans",
	subsets: ["latin"],
});

const geistMono = Geist_Mono({
	variable: "--font-geist-mono",
	subsets: ["latin"],
});

const notoSansSC = Noto_Sans_SC({
	variable: "--font-noto-sans-sc",
	subsets: ["latin"],
	weight: ["400", "500", "700"],
});

export async function generateMetadata() {
	return generatePageMetadata();
}

export default function RootLayout({ children }: { children: ReactNode }) {
	return (
		<html lang="zh" suppressHydrationWarning>
			<head>
				<link rel="icon" href={`${process.env.NEXT_PUBLIC_BASE_PATH || ""}/logo.svg`} type="image/svg+xml" />
				<link rel="apple-touch-icon" href={`${process.env.NEXT_PUBLIC_BASE_PATH || ""}/logo.svg`} />
			</head>
			<body
				className={`${geistSans.variable} ${geistMono.variable} ${notoSansSC.variable} antialiased`}
			>
				<GoogleAnalytics />
				<UmamiAnalytics />
				<ThemeProvider>
					{children}
					<Toaster />
				</ThemeProvider>
			</body>
		</html>
	);
}
