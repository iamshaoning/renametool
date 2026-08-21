export default function NotFound() {
	return (
		<html lang="zh">
			<body
				style={{
					display: "flex",
					flexDirection: "column",
					alignItems: "center",
					justifyContent: "center",
					minHeight: "100vh",
					margin: 0,
					fontFamily: "system-ui, sans-serif",
					background: "#07090d",
					color: "#ffffff",
				}}
			>
				<h1 style={{ fontSize: 48, margin: 0 }}>404</h1>
				<p style={{ fontSize: 18, color: "#b8c2d6" }}>页面不存在</p>
				<a
					href="/"
					style={{
						marginTop: 16,
						padding: "10px 20px",
						borderRadius: 8,
						background: "#1e90ff",
						color: "#ffffff",
						textDecoration: "none",
					}}
				>
					返回首页
				</a>
			</body>
		</html>
	);
}
