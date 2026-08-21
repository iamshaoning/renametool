export const GUIDE_LOCALES = ["zh"] as const;

export type GuideLocale = (typeof GUIDE_LOCALES)[number];

export type GuideCategory = "getting-started" | "photos" | "patterns" | "numbering" | "media";

export interface GuideExample {
	before: string;
	after: string;
	note?: string;
}

export interface GuideImage {
	src: string;
	alt: string;
	caption?: string;
}

export interface GuideSection {
	title: string;
	body: string[];
	steps?: string[];
	examples?: GuideExample[];
	image?: GuideImage;
}

export interface LocalizedGuideContent {
	title: string;
	description: string;
	intro: string;
	categoryLabel: string;
	sections: GuideSection[];
}

export interface Guide {
	slug: string;
	category: GuideCategory;
	updatedAt: string;
	readingTime: number;
	relatedSlugs: string[];
	content: Record<GuideLocale, LocalizedGuideContent>;
}

export interface LocalizedGuide extends Omit<Guide, "content"> {
	locale: GuideLocale;
	title: string;
	description: string;
	intro: string;
	categoryLabel: string;
	sections: GuideSection[];
}

export const guideIndexCopy: Record<
	GuideLocale,
	{
		title: string;
		description: string;
		eyebrow: string;
		heading: string;
		intro: string;
		allGuides: string;
		featured: string;
		updated: string;
		minRead: string;
		relatedGuides: string;
		startRenaming: string;
		readGuide: string;
		backToGuides: string;
		ctaTitle: string;
		ctaDesc: string;
	}
> = {
	zh: {
		title: "使用指南 - Rename.Tools",
		description:
			"Rename.Tools 批量文件重命名实用指南：正则表达式、序号、照片整理、音乐库和剧集文件名整理。",
		eyebrow: "使用指南",
		heading: "实用的文件重命名指南",
		intro: "学习如何用实时预览和本地处理工作流，安全整理照片、媒体库、下载文件和归档文件夹。",
		allGuides: "全部指南",
		featured: "精选工作流",
		updated: "更新于",
		minRead: "分钟阅读",
		relatedGuides: "相关指南",
		startRenaming: "开始重命名",
		readGuide: "阅读指南",
		backToGuides: "返回指南",
		ctaTitle: "准备试试这个工作流？",
		ctaDesc: "打开 Rename.Tools，先添加几个示例文件，用预览确认每条规则后再处理真实文件名。",
	},
};

export const guides: Guide[] = [
	{
		slug: "batch-file-rename-basics",
		category: "getting-started",
		updatedAt: "2026-05-22",
		readingTime: 9,
		relatedSlugs: ["sequence-file-numbering", "regex-batch-rename"],
		content: {
			zh: {
				title: "批量重命名入门：导入、预览、执行",
				description:
					"学习 Rename.Tools 最稳妥的工作流：添加文件、构建规则链、检查预览、处理冲突，并在本地执行重命名。",
				intro:
					"批量重命名最好像代码变更一样先审查：先导入少量文件，一次添加一条规则，预览确认无误后再执行。",
				categoryLabel: "入门",
				sections: [
					{
						title: "先导入少量文件熟悉界面",
						body: [
							"最稳妥的方式是从少量文件开始：先导入几张照片、一个文档和一个视频文件名。混合示例能帮你发现规则是否过宽，是否误伤了不该处理的文件。",
							"导入的文件只会在浏览器本地处理，不会上传到任何服务器。你可以放心地在预览中反复调整规则，直到确认无误。",
						],
						image: {
							src: "/guides/screenshots/app-sample-files.png",
							alt: "Rename.Tools 文件面板，已导入 6 个文件并显示未变化的预览",
							caption: "先导入少量文件熟悉界面，再处理真实目录。",
						},
						steps: [
							"打开应用，在文件面板点击“添加文件”。",
							"选择几个不同扩展名的文件，例如图片、文档和视频。",
							"确认文件列表显示正确，并检查预览面板的初始状态。",
							"保持作用域为“名称”，避免第一轮规则意外修改扩展名。",
						],
					},
					{
						title: "一次只添加一条规则",
						body: [
							"可靠的批量重命名应该由一组小而可检查的步骤组成。先添加清理规则，检查预览，再添加编号规则。如果预览出现异常，最近添加的规则通常就是排查入口。",
							"这个例子会移除相机前缀，并添加补零序号。它刻意保持简单，重点是让你理解规则链如何逐步改变预览结果。",
						],
						image: {
							src: "/guides/screenshots/app-sequence-preview.png",
							alt: "Rename.Tools 规则链中包含查找替换和序号规则，预览面板实时显示结果",
							caption:
								"逐条添加规则，并在每一步后检查预览。这里的序号规则更新了全部 6 个示例文件。",
						},
						steps: [
							"点击“添加规则”，选择“查找替换”。",
							"将“查找”设为 IMG_，“替换为”留空，用于移除前缀。",
							"再次点击“添加规则”，选择“序号”。",
							"保持数字序号，起始值为 1，步长为 1，补零位数为 3。",
							"查看预览面板，确认编号后的结果符合你想要的顺序。",
						],
						examples: [
							{
								before: "IMG_0421.jpg",
								after: "2026-05-22_001.jpg",
								note: "移除 IMG_，插入日期前缀，再添加补零序号。",
							},
							{
								before: "IMG_0422.jpg",
								after: "2026-05-22_002.jpg",
							},
						],
					},
					{
						title: "像清单一样阅读预览",
						body: [
							"预览面板不只是展示效果，它就是执行前的审查步骤。先看原文件名，再看新文件名，重点找出和你预期不一致的变化。",
							"文件较多时使用“仅受影响”视图，只看真正会被改名的文件。如果冲突数量不为零，一定切到“仅冲突”视图，因为重复目标名必须先处理。",
						],
						steps: [
							"检查每个变化后的文件是否仍保留正确扩展名。",
							"检查是否出现空文件名或只有数字的文件名。",
							"检查编号顺序是否符合你想要的文件顺序。",
							"执行前切换到“仅冲突”视图，确认没有重复目标名。",
						],
						examples: [
							{
								before: "report (final).docx",
								after: "005report (final).docx",
								note: "这可能在技术上正确，但不一定符合目标；它提醒你序号规则影响了文档文件。",
							},
						],
					},
					{
						title: "确认后直接执行，出错立即撤销",
						body: [
							"在 Chromium 浏览器中，通过 File System Access API 选择的真实文件可以直接重命名。预览和冲突检查都确认干净后，点击“执行重命名”即可。",
							"执行后如果立即发现错误，优先使用内置撤销功能恢复。撤销会记录最近一次执行结果，可以一键还原。",
						],
						steps: [
							"处理真实文件时，先从小文件夹或复制出来的测试文件夹开始。",
							"执行前再次确认预览中的新旧文件名映射符合预期。",
							"只有当预览和冲突检查都干净时，才点击“执行重命名”。",
							"执行后如果立即发现错误，使用撤销按钮还原。",
						],
					},
					{
						title: "首次使用常见错误",
						body: [
							"大多数错误来自规则影响了过多文件。如果只想处理照片，应先筛选文件列表，或让规则只匹配照片类文件名。",
							"另一个常见错误是选择了完整文件名作用域，而实际只想修改名称部分。多数工作流都应保持作用域为“名称”；只有明确要修改扩展名或完整文件名时再切换。",
						],
						examples: [
							{
								before: "movie.name.s01e03.1080p.mkv",
								after: "006movie.name.s01e03.1080p.mkv",
								note: "全局序号会影响所有选中文件。不同文件类型需要不同命名规则时，建议筛选或分批处理。",
							},
						],
					},
				],
			},
		},
	},
	{
		slug: "organize-photos-by-date-sequence",
		category: "photos",
		updatedAt: "2026-05-22",
		readingTime: 10,
		relatedSlugs: ["batch-file-rename-basics", "sequence-file-numbering"],
		content: {
			zh: {
				title: "按日期和序号整理照片",
				description: "用日期前缀和补零序号重命名相机照片，让相册保持可排序、易浏览、易查找。",
				intro:
					"相机文件名通常唯一，但不够直观。日期加序号的格式能保持时间顺序，也方便跨设备搜索和归档。",
				categoryLabel: "照片整理",
				sections: [
					{
						title: "选择稳定的照片命名格式",
						body: [
							"好的照片文件名应该离开 Rename.Tools 后依然清楚。建议把日期放在最前面，然后是补零序号，最后可选地点、客户、活动或相机标签。",
							"年份用四位，月份和日期用两位。这样在 Finder、Explorer、网盘、NAS 或备份工具中按字母排序时，也会自然符合时间顺序。",
							"先决定文件名要表达什么：拍摄时间、归属地点，还是两者都要。个人相册通常日期加地点就够了；客户项目则建议加入项目名或拍摄主题，方便交付后识别。",
						],
						examples: [
							{
								before: "DSC_0007.JPG",
								after: "2026-05-22_001_tokyo.JPG",
							},
							{
								before: "IMG_1842.HEIC",
								after: "2026-05-22_002_tokyo.HEIC",
							},
						],
					},
					{
						title: "构建规则链",
						body: [
							"如果相机前缀没有实际意义，可以用查找替换或删除规则清理掉。然后用带补零的序号规则，让每张照片获得稳定编号。",
							"如果相册需要保留原始相机顺序，编号前按名称排序。若照片来自手机和相机混合来源，可以按修改时间排序；加载元数据后，也可以优先使用 EXIF 拍摄时间。",
						],
						image: {
							src: "/guides/screenshots/app-sequence-preview.png",
							alt: "Rename.Tools 照片清理工作流，通过序号规则转换相机文件名",
							caption: "序号预览能帮你在处理整个照片文件夹前确认排序是否正确。",
						},
						steps: [
							"只导入照片文件夹，或先按图片扩展名筛选文件列表。",
							"将作用域保持为“名称”，避免 .jpg、.heic 等扩展名被修改。",
							"添加清理规则，移除 IMG_、DSC_、PXL_ 等相机前缀。",
							"添加序号规则，把补零位数设置为 3 或 4。",
							"模板使用 {date}_{n}_trip；如果已加载 EXIF，可使用 {exif.date}_{n}_trip。",
							"除非明确要修改扩展名，否则规则作用域保持为仅文件名。",
						],
					},
					{
						title: "什么时候使用 EXIF 元数据",
						body: [
							"如果照片带有 EXIF 拍摄时间，建议先加载元数据，再使用 EXIF 日期变量，而不是今天的日期。文件被复制、下载、从手机导出或后期编辑过时，这一点尤其有用。",
							"EXIF 并不总是存在。截图、社交平台导出的图片、后期处理后的图片，以及部分 HEIC 转换文件都可能缺少或改变元数据。因此最好准备一个不依赖 EXIF 的备用规则链，比如使用文件夹名、今天日期或手动活动标签。",
							"当 EXIF 日期和文件修改时间不一致时，选择更符合整理目标的那个。旅行相册通常更适合拍摄日期；交付目录则可能更适合导出日期。",
						],
					},
					{
						title: "执行前的照片检查清单",
						body: [
							"执行前先检查预览的前几行和最后几行。这样最容易发现排序错误，尤其是原文件名里包含不同长度数字时。",
							"如果预览里混入了非照片文件，先停下来筛选。把照片、文档和视频放进同一个序号规则里，虽然能生成合法文件名，但后续并不好管理。",
						],
						steps: [
							"确认每个目标名都以预期日期开头。",
							"确认序号补零一致，例如 001、002、003。",
							"确认 .jpg、.png、.heic、raw 类扩展名没有被改写。",
							"切到“仅受影响”视图，检查是否有本应保持不变的文件也被改名。",
						],
						examples: [
							{
								before: "Vacation/IMG_0421.jpg",
								after: "Vacation/2026-05-22_001_tokyo.jpg",
								note: "保留活动文件夹，再添加可排序文件名，可以同时拥有两层清晰组织结构。",
							},
						],
					},
					{
						title: "必要时按来源拆分照片",
						body: [
							"手机照片、相机照片、截图、RAW 导出和修图成片往往需要不同命名规则。如果一条规则链开始变得很复杂，通常应该拆成更小批次，而不是强行覆盖所有情况。",
							"例如相机照片可以使用 EXIF 日期加序号，截图可以使用修改日期，修图导出则更适合项目标签加版本号。小批次更容易得到干净名称，也更容易检查预览。",
						],
						steps: [
							"用文件夹或筛选器分开相机照片、截图和修图导出。",
							"为拍摄日期命名和导出日期命名分别保存预设。",
							"如果 RAW 和成片不应共享同一序号，就分批处理。",
							"只有当文件夹名信息不够时，才在文件名里添加简短活动标签。",
						],
						examples: [
							{
								before: "Edits/final_export_3.jpg",
								after: "client-a_2026-05-22_v03.jpg",
								note: "修图导出通常比拍摄时间更需要版本标签。",
							},
						],
					},
				],
			},
		},
	},
	{
		slug: "regex-batch-rename",
		category: "patterns",
		updatedAt: "2026-05-22",
		readingTime: 11,
		relatedSlugs: ["batch-file-rename-basics", "sequence-file-numbering"],
		content: {
			zh: {
				title: "用正则表达式批量重命名",
				description:
					"学习实用的正则重命名模式，用于清理杂乱字符、重排日期、提取文件名中的关键信息。",
				intro:
					"当文件名具有共同模式时，正则是最强大的重命名规则。简单查找替换无法精确描述变更时，就适合使用正则。",
				categoryLabel: "模式规则",
				sections: [
					{
						title: "从一个清晰模式开始",
						body: [
							"正则最适合处理格式一致的文件名。只匹配你真正想改变的部分，并让替换结果保持可读。窄一点的模式通常比试图一次解决所有文件名的复杂模式更安全。",
							"如果需要保留并重排有用片段，就使用捕获组。在 Rename.Tools 里，替换文本可以用 $1、$2 等引用捕获到的内容。",
							"写正则前，先用自然语言描述文件名结构：日期在哪里，标题在哪里，剧集编号在哪里，哪些部分要删除。这个描述往往就是正则模式的雏形。",
						],
						examples: [
							{
								before: "2026-05-22 invoice client-a.pdf",
								after: "invoice_client-a_2026-05-22.pdf",
								note: "捕获日期和标题，再交换顺序。",
							},
							{
								before: "movie.name.s01e03.1080p.mkv",
								after: "movie name S01E03.mkv",
							},
						],
					},
					{
						title: "常用正则重命名模式",
						body: [
							"下面这些模式适合作为起点。先用少量文件预览确认，再应用到包含大量文件的文件夹。如果一个正则看起来很脆弱，可以拆成一条正则规则加一条简单查找替换规则。",
							"Flags 字段也要有意识地设置。大小写不统一时用 i，需要替换所有出现位置时用 g；除非处理多行文本，否则通常不需要 m。",
						],
						image: {
							src: "/guides/screenshots/app-regex-preview.png",
							alt: "Rename.Tools 正则规则提取视频剧集编号，并在预览中显示更新结果",
							caption: "正则规则最适合配合预览检查：能直接看到捕获内容和替换结果是否符合预期。",
						},
						steps: [
							"点击“添加规则”，选择“正则替换”。",
							"在“正则模式”里输入匹配规则，在“替换为”里输入目标格式。",
							"用预览确认哪些文件发生变化，哪些文件保持不变。",
							"移除方括号备注：\\s*\\[[^\\]]+\\]",
							"把开头日期移到末尾：^(\\d{4}-\\d{2}-\\d{2})\\s+(.+)$ -> $2_$1",
							"统一剧集大小写：s(\\d+)e(\\d+) -> S$1E$2",
							"压缩重复空格：\\s+ -> 单个空格",
						],
					},
					{
						title: "让正则保持安全",
						body: [
							"除非确实要替换全部内容，否则避免使用过宽的 .*。如果替换后出现空名称、重复名称，或删除了比预期更多的文本，先停下来缩小匹配范围。",
							"当一条正则难以判断时，把工作流拆成两三条更简单的规则。这样预览更容易检查，之后保存为预设也更容易理解。",
							"正则不一定要放在第一条规则。很多时候，最清晰的流程是先用查找替换清理明显文本，再用正则完成结构转换，最后用大小写或序号规则收尾。",
						],
					},
					{
						title: "用预览调试正则",
						body: [
							"正则不生效时，不要马上把它写得更复杂。先确认它到底有没有匹配到正确内容。一个实用技巧是暂时把替换文本设为 MATCH_$1，这样可以直接在预览里看到捕获到了什么。",
							"当捕获组正确后，再恢复真正的替换格式。这个方法处理单个文件时看似慢，但面对几百个文件时反而更快、更稳。",
						],
						steps: [
							"先用 3 到 5 个代表性文件名测试正则。",
							"临时把替换结果设为 match_$1_$2 之类的可见标记。",
							"确认预览中显示的捕获内容符合预期。",
							"捕获组正确后，再恢复最终替换格式。",
							"切到“仅受影响”视图，确认无关文件没有被匹配。",
						],
						examples: [
							{
								before: "client-a_invoice_2026-05-22_final.pdf",
								after: "invoice_client-a_2026-05-22.pdf",
								note: "有针对性的正则能保留客户、文档类型和日期，同时移除临时 final 标记。",
							},
						],
					},
					{
						title: "知道什么时候不该用正则",
						body: [
							"正则很强，但不一定总是最清晰的工具。如果只是替换一个固定词，用查找替换即可；如果只是编号，用序号规则；如果只是大小写转换，用大小写规则更合适。",
							"最好的规则链往往是组合式的：简单规则处理简单修改，正则只负责结构转换，最后再通过预览确认结果。这样保存下来的预设也更容易维护、更安全。",
						],
						steps: [
							"固定词或分隔符替换优先使用查找替换。",
							"括号、符号等重复杂乱内容可先用删除/清理规则。",
							"需要捕获并重排有用片段时，再使用正则替换。",
							"正则预设一定要在多个文件名变体上测试后再保存。",
						],
						examples: [
							{
								before: "My Vacation Photos.jpg",
								after: "my-vacation-photos.jpg",
								note: "这是大小写/风格转换任务，不需要正则。",
							},
						],
					},
				],
			},
		},
	},
	{
		slug: "sequence-file-numbering",
		category: "numbering",
		updatedAt: "2026-05-22",
		readingTime: 9,
		relatedSlugs: ["organize-photos-by-date-sequence", "batch-file-rename-basics"],
		content: {
			zh: {
				title: "用序号规则生成稳定文件名",
				description: "使用补零序号、排序和按文件夹编号，生成在任何地方都能保持有序的文件名。",
				intro:
					"序号看似简单，但设置很关键。补零、排序和作用域会决定文件导出、上传或归档后是否依然稳定。",
				categoryLabel: "序号编号",
				sections: [
					{
						title: "用补零保证可靠排序",
						body: [
							"如果不补零，某些文件管理器可能把 10 排在 2 前面。补零能让所有数字长度一致，让字母排序和数字顺序保持一致。",
							"小相册可用 2 位，数百个文件建议 3 位，如果文件夹未来还会增长，可以用 4 位。多留一位通常没什么坏处；位数太少，后续反而可能需要再次整理。",
							"当文件要上传网盘、交付客户、导入剪辑软件或放入只按字母排序的归档系统时，补零尤其重要。",
						],
						image: {
							src: "/guides/screenshots/app-sequence-preview.png",
							alt: "Rename.Tools 序号规则预览，示例文件被添加补零编号",
							caption: "补零效果会立刻显示在预览里，因此可以在执行前发现排序问题。",
						},
						examples: [
							{
								before: "photo 1.jpg, photo 2.jpg, photo 10.jpg",
								after: "001_photo.jpg, 002_photo.jpg, 010_photo.jpg",
							},
						],
					},
					{
						title: "选择合适的序号作用域",
						body: [
							"全局编号适合把整个批次当成一个有序集合。按文件夹编号更适合相册、章节、导出目录或客户文件夹，让每个文件夹都从 001 开始。",
							"按扩展名或按文件类型编号适合混合文件夹。比如截图和视频可以分别编号，而不是混在同一个序列里。",
						],
						steps: [
							"单个相册或单次导出批次使用全局作用域。",
							"每个文件夹都要单独编号时使用按文件夹作用域。",
							"图片、视频、文档需要分别编号时使用按扩展名作用域。",
							"导入顺序不可靠时，先排序再编号。",
							"处理 file1、file2、file10 这类名称时，开启自然排序。",
						],
					},
					{
						title: "把序号和模板组合",
						body: [
							"序号规则不只是添加数字。可以组合 {n}、{name}、日期、文件夹名或元数据变量，生成结构清晰且可读的新名称。",
							"模板最重要的是把稳定排序字段放在前面。比如 {date}_{n}_{name} 会先按日期排序，再按序号排序；而 {name}_{n}_{date} 更像自然语言，但排序时会先按原名称分组。",
						],
						examples: [
							{
								before: "scan.jpg",
								after: "archive_2026_001_scan.jpg",
							},
						],
					},
					{
						title: "原始编号有意义时不要重排",
						body: [
							"有些文件本来就带有重要编号：扫描页、导出帧、章节文件或连拍照片。这种情况下，不一定要按导入顺序重新生成序号。",
							"当文件名里的数字就是顺序来源时，可以使用“保留原始编号”。Rename.Tools 会提取原编号，统一补零，并保持新旧文件名之间的关系清晰。",
						],
						steps: [
							"在序号规则中开启“保留原始编号”。",
							"简单文件名可以保留提取模式 (\\d+)。",
							"如果文件名里有多个数字，使用更具体的模式，例如 page-(\\d+)。",
							"预览提取失败的文件；它们会回退到普通序号逻辑。",
						],
						examples: [
							{
								before: "page-7-scan.jpg",
								after: "page_007_scan.jpg",
								note: "保留原始页码可以避免打乱文档顺序。",
							},
						],
					},
					{
						title: "先决定顺序，再添加编号",
						body: [
							"序号是否有用，取决于背后的排序是否正确。导入顺序很方便，但在不同浏览器、文件夹或操作系统中不一定稳定。",
							"为了结果可预测，编号前应明确选择排序方式。相机文件通常适合按名称排序，文档批次可以按修改时间排序，按扩展名排序则适合不同文件类型分别编号。",
						],
						steps: [
							"当导入顺序没有意义时，开启“编号前排序”。",
							"相机类文件名通常选择按文件名排序。",
							"按创建或导出顺序整理文档时，可选择修改时间。",
							"检查预览中的第一个和最后一个编号，确认顺序正确。",
						],
						examples: [
							{
								before: "scan10.jpg, scan2.jpg, scan1.jpg",
								after: "001_scan1.jpg, 002_scan2.jpg, 010_scan10.jpg",
								note: "自然排序可以避免 scan10 被排在 scan2 前面。",
							},
						],
					},
				],
			},
		},
	},
	{
		slug: "organize-music-video-files",
		category: "media",
		updatedAt: "2026-05-22",
		readingTime: 10,
		relatedSlugs: ["regex-batch-rename", "sequence-file-numbering"],
		content: {
			zh: {
				title: "整理音乐库与剧集文件名",
				description:
					"用元数据变量、序号规则和正则模式，清理音乐文件和视频剧集文件名。",
				intro:
					"媒体文件常常带着杂乱的发布信息。Rename.Tools 可以把它们整理成适合播放器、媒体服务器和共享文件夹的稳定名称。",
				categoryLabel: "媒体整理",
				sections: [
					{
						title: "音乐库命名",
						body: [
							"专辑文件名应该保留曲目顺序，并且离开播放器后依然可读。如果音频标签可用，先加载元数据，再使用艺术家、标题、专辑或曲目号变量。",
							"一个实用格式是曲目号在前，随后是艺术家和标题。这样在文件夹里能正确排序，复制到 U 盘、手机、DJ 曲库或媒体服务器后也能看懂。",
							"如果文件标签并不可靠，可以先基于现有文件名和序号整理。即使不依赖元数据，也可以清理分隔符、统一大小写，并保留曲目顺序。",
						],
						steps: [
							"曲目顺序重要时，一次只导入一个专辑。",
							"如果音频标签可靠，先加载元数据。",
							"使用类似 {media.track}. {media.artist} - {media.title} 的模板。",
							"保持作用域为“名称”，避免音频扩展名被修改。",
							"执行前预览是否有缺失的艺术家或标题值。",
						],
						examples: [
							{
								before: "love story.mp3",
								after: "01. Taylor Swift - Love Story.mp3",
							},
							{
								before: "track_07.flac",
								after: "07. Artist - Song Title.flac",
							},
						],
					},
					{
						title: "视频与剧集清理",
						body: [
							"视频文件常包含点号、清晰度标签、发布组名称和不统一的集数大小写。可以用正则提取剧名和集数代码，再用查找替换清理分隔符。",
							"剧集文件名通常统一为 S01E03 这种格式即可，不需要依赖外部数据源。用正则保留集数编号，再统一大小写和分隔符，就能满足播放器和媒体服务器的识别需求。",
							"对媒体服务器来说，一致性比聪明的命名更重要。给一个媒体库选择一种格式，并跨季复用。",
						],
						image: {
							src: "/guides/screenshots/app-regex-preview.png",
							alt: "Rename.Tools 正则预览，把杂乱的剧集文件名转换成更清晰的 S01E03 名称",
							caption: "先从一个剧集模式开始，确认预览正确后，再应用到整季文件。",
						},
						steps: [
							"把点号或下划线替换为空格，统一分隔符。",
							"用正则保留 S01E03 这种剧集编号。",
							"在不需要时移除 720p、1080p、WEB-DL、BluRay 等质量标签。",
							"在正则提取之后再使用 Title Case，让剧名更易读。",
							"执行前先预览完整季的文件。",
						],
						examples: [
							{
								before: "show.name.s01e03.1080p.web-dl.mkv",
								after: "Show Name S01E03.mkv",
							},
						],
					},
					{
						title: "保持媒体服务器兼容",
						body: [
							"使用统一分隔符，除非你在其他工具里真正转换了格式，否则不要修改扩展名。Rename.Tools 只改文件名，不转换媒体格式。",
							"共享媒体库更适合可预测的格式，而不是过于聪明的命名。能正确排序、容易被其他工具解析的普通格式，更适合长期维护。",
							"大规模重命名前，先测试一个专辑或一季。媒体库经常有特殊情况：bonus track、特别篇、预告片、花絮、字幕、多段剧集等。",
						],
					},
					{
						title: "字幕和花絮要分开处理",
						body: [
							"字幕文件需要和视频文件保持相同基础名称，播放器才能自动识别。不要把只适合视频的清理规则直接套到 .srt 或 .ass 文件上，除非结果仍然和视频名称匹配。",
							"预告片、访谈、sample、幕后花絮等 extras 通常不符合 S01E03 规则，建议筛选成单独批次处理。",
						],
						steps: [
							"视频和字幕规则不同时，分别筛选处理。",
							"重命名剧集后，检查字幕基础名称是否仍和视频一致。",
							"花絮文件可以放入单独文件夹，或添加 extras、trailer 等清晰后缀。",
							"处理混合媒体文件夹前，先在预览中确认新旧文件名映射。",
						],
						examples: [
							{
								before: "show.name.s01e03.1080p.en.srt",
								after: "Show Name S01E03.en.srt",
								note: "字幕基础名仍然和清理后的剧集名称保持一致。",
							},
						],
					},
				],
			},
		},
	},
];

export function isIndexableGuideLocale(locale: string): locale is GuideLocale {
	return GUIDE_LOCALES.includes(locale as GuideLocale);
}

export function getGuideLocale(locale: string): GuideLocale {
	return "zh";
}

export function getGuideIndexCopy(locale: string) {
	return guideIndexCopy[getGuideLocale(locale)];
}

export function getAllGuides(locale: string): LocalizedGuide[] {
	const guideLocale = getGuideLocale(locale);
	return guides.map((guide) => localizeGuide(guide, guideLocale));
}

export function getGuideBySlug(slug: string, locale: string): LocalizedGuide | undefined {
	const guide = guides.find((item) => item.slug === slug);
	return guide ? localizeGuide(guide, getGuideLocale(locale)) : undefined;
}

export function getRelatedGuides(guide: LocalizedGuide, locale: string): LocalizedGuide[] {
	return guide.relatedSlugs
		.map((slug) => getGuideBySlug(slug, locale))
		.filter((item): item is LocalizedGuide => item != null);
}

export function getGuidePrimaryImage(guide: LocalizedGuide): GuideImage | undefined {
	return guide.sections.find((section) => section.image)?.image;
}

export function getGuideSlugs(): string[] {
	return guides.map((guide) => guide.slug);
}

function localizeGuide(guide: Guide, locale: GuideLocale): LocalizedGuide {
	const content = guide.content[locale];
	return {
		slug: guide.slug,
		category: guide.category,
		updatedAt: guide.updatedAt,
		readingTime: guide.readingTime,
		relatedSlugs: guide.relatedSlugs,
		locale,
		title: content.title,
		description: content.description,
		intro: content.intro,
		categoryLabel: content.categoryLabel,
		sections: content.sections,
	};
}
