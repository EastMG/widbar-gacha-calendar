#!/usr/bin/env node
// 从 gacha-calendar-core 抽取 28 款元信息 → data/catalog.json
//
// 为什么需要这个工具：v0 的数据源只有 feed（core 的 Result JSON + meta），
// feed 里的 GameRecord 没有 icon / 展示顺序 / 出厂默认隐藏 / 时区，这些属于
// core 的来源注册表（SOURCES），必须在构建期抽出来固化成 catalog，运行期不再依赖 core。
//
// 只读引用 core，不复制其抓取/解析逻辑（仓库硬边界）。
//
// 用法：
//   node tools/gen-catalog.mjs           # 生成/更新 data/catalog.json
//   node tools/gen-catalog.mjs --check   # CI 门禁：catalog 与 core 不一致时退出码 1
//
// core 路径解析顺序（第一个存在的即为准）：
//   1. 环境变量 GACHA_CORE_PATH（指向 core.mjs）
//   2. 本仓库 node_modules/gacha-calendar-core/core.mjs
//   3. 兄弟仓库 ../gacha-calendar/node_modules/gacha-calendar-core/core.mjs（当前开发环境）
//
// 幂等：源码未变时不改写文件（保留原 generatedAt），--check 才能稳定通过。

import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const REPO = path.resolve(HERE, "..");
const OUT_FILE = path.join(REPO, "data", "catalog.json");
const CHECK = process.argv.includes("--check");

const CORE_CANDIDATES = [
	process.env.GACHA_CORE_PATH,
	path.join(REPO, "node_modules", "gacha-calendar-core", "core.mjs"),
	path.resolve(REPO, "..", "gacha-calendar", "node_modules", "gacha-calendar-core", "core.mjs"),
].filter(Boolean);

// 出厂默认隐藏的 5 款（用于和 core 核对；不一致时告警但不改数据）
const EXPECTED_DEFAULT_HIDDEN = ["bh3", "ournotes", "ournotes-global", "czn", "ddlezj"];

function findCore() {
	for (const candidate of CORE_CANDIDATES) {
		if (fs.existsSync(candidate)) return path.resolve(candidate);
	}
	throw new Error(
		"找不到 gacha-calendar-core。请先在有核心包的仓库执行 npm install，" +
			"或设置 GACHA_CORE_PATH 指向 core.mjs。\n尝试过：\n  " + CORE_CANDIDATES.join("\n  "),
	);
}

function readCoreVersion(corePath) {
	try {
		const pkg = JSON.parse(fs.readFileSync(path.join(path.dirname(corePath), "package.json"), "utf8"));
		return typeof pkg.version === "string" ? pkg.version : "";
	} catch {
		return "";
	}
}

// 把 core 的时区声明（IANA 名 / "UTC" / "+540"）换算成 UTC 偏移分钟数。
// 为什么存分钟数：外壳不需要 TzDatabase，直接用于显示/换算即可；
// 现有 28 款的时区都无夏令时，按生成时刻换算即可（generatedAt 变了会重算）。
function tzToMinutes(tz, at) {
	if (!tz) return 480; // core 默认上海时区
	const fixed = /^([+-])(\d{1,3})$/.exec(tz.trim());
	if (fixed) {
		const minutes = Number(fixed[2]);
		return fixed[1] === "-" ? -minutes : minutes;
	}
	try {
		const fmt = new Intl.DateTimeFormat("en-US", {
			timeZone: tz,
			hourCycle: "h23",
			year: "numeric",
			month: "2-digit",
			day: "2-digit",
			hour: "2-digit",
			minute: "2-digit",
			second: "2-digit",
		});
		const parts = Object.fromEntries(fmt.formatToParts(at).map((p) => [p.type, p.value]));
		const wall = Date.UTC(
			Number(parts.year),
			Number(parts.month) - 1,
			Number(parts.day),
			Number(parts.hour),
			Number(parts.minute),
			Number(parts.second),
		);
		return Math.round((wall - at.getTime()) / 60000);
	} catch {
		return 480;
	}
}

// engine.__test.SOURCES 就是 core 的来源注册表字面量（官方测试入口，见姊妹仓库 docs/07）。
// 极端情况下 __test 不可用，则退化为在 bundle 文本里按 id 找邻近块——只读，不改 core。
function sourceFacts(engine, coreText) {
	const sources = engine?.__test?.SOURCES;
	if (Array.isArray(sources) && sources.length > 0) {
		return { mode: "__test.SOURCES", map: new Map(sources.map((s) => [s.id, s])) };
	}
	return { mode: "bundle-regex", map: null, text: coreText };
}

function pickFacts(facts, id) {
	if (facts.map) {
		const src = facts.map.get(id);
		return { defaultHidden: !!src?.defaultHidden, tz: src?.tz ?? "", icon: src?.icon ?? "" };
	}
	// 退化路径：在 bundle 里以 id 为锚点，取邻近一段做正则匹配
	const text = facts.text ?? "";
	const idx = text.indexOf(`id:"${id}"`);
	const block = idx < 0 ? "" : text.slice(Math.max(0, idx - 1500), idx + 2500);
	const defaultHidden = /defaultHidden\s*:\s*(!0|true)/.test(block);
	const tz = /tz\s*:\s*"([^"]+)"/.exec(block)?.[1] ?? "";
	const icon = /icon\s*:\s*"([^"]+)"/.exec(block)?.[1] ?? "";
	return { defaultHidden, tz, icon };
}

async function main() {
	const corePath = findCore();
	const coreUrl = pathToFileURL(corePath).href;
	const core = await import(coreUrl);
	if (typeof core.createEngine !== "function") throw new Error("core.mjs 没有导出 createEngine");

	// 建引擎时给最小桩：只调用 listGames，不联网、不读写存储。
	const engine = core.createEngine({
		transport: async () => {
			throw new Error("gen-catalog 不联网");
		},
		storage: { get: () => null, set: () => {}, keys: () => [] },
		now: () => Date.now(),
	});

	const listed = await engine.listGames();
	if (!Array.isArray(listed) || listed.length === 0) throw new Error("engine.listGames() 返回空");

	const facts = sourceFacts(engine, fs.readFileSync(corePath, "utf8"));
	const generatedAt = Date.now();
	const at = new Date(generatedAt);
	const games = listed.map((g, index) => {
		const f = pickFacts(facts, g.id);
		return {
			id: g.id,
			name: g.name ?? "",
			icon: g.icon || f.icon || "",
			order: index,
			defaultHidden: f.defaultHidden,
			tz: tzToMinutes(f.tz, at),
		};
	});

	const hiddenIds = games.filter((g) => g.defaultHidden).map((g) => g.id);
	const expected = [...EXPECTED_DEFAULT_HIDDEN].sort().join(",");
	const actual = [...hiddenIds].sort().join(",");
	if (expected !== actual) {
		console.warn(`⚠️ 出厂默认隐藏与预期不一致：期望 [${expected}]，实际 [${actual}]（以 core 为准）`);
	}

	const noIcon = games.filter((g) => !g.icon).map((g) => g.id);
	if (noIcon.length > 0) console.warn(`⚠️ 以下条目拿不到 icon：${noIcon.join(", ")}`);

	const catalog = {
		schemaVersion: 1,
		generatedAt,
		coreVersion: readCoreVersion(corePath),
		games,
	};

	// 幂等：内容未变则沿用旧 generatedAt，且不改写文件。
	const nextText = JSON.stringify(catalog, null, 2) + "\n";
	let prevText = null;
	if (fs.existsSync(OUT_FILE)) prevText = fs.readFileSync(OUT_FILE, "utf8");
	let prev = null;
	if (prevText) {
		try {
			prev = JSON.parse(prevText);
		} catch {
			prev = null;
		}
	}
	if (prev && prev.coreVersion === catalog.coreVersion && prev.schemaVersion === catalog.schemaVersion) {
		if (JSON.stringify({ ...prev, generatedAt: 0 }) === JSON.stringify({ ...catalog, generatedAt: 0 })) {
			catalog.generatedAt = prev.generatedAt;
		}
	}
	const stableText = JSON.stringify(catalog, null, 2) + "\n";

	if (CHECK) {
		if (prevText === stableText) {
			console.log(`✓ catalog 与 core 一致（${games.length} 款，core ${catalog.coreVersion}，来源 ${facts.mode}）`);
			return;
		}
		console.error("✗ data/catalog.json 与 core 不一致，请运行 `node tools/gen-catalog.mjs` 重新生成。");
		process.exitCode = 1;
		return;
	}

	if (prevText === stableText) {
		console.log(`✓ data/catalog.json 已是最新（${games.length} 款，core ${catalog.coreVersion}，来源 ${facts.mode}）`);
		return;
	}
	fs.mkdirSync(path.dirname(OUT_FILE), { recursive: true });
	fs.writeFileSync(OUT_FILE, stableText, "utf8");
	console.log(
		`✓ 已写入 ${path.relative(REPO, OUT_FILE)}：${games.length} 款，core ${catalog.coreVersion}，` +
			`默认隐藏 ${hiddenIds.length} 款（${hiddenIds.join("、")}），icon 缺失 ${noIcon.length} 款，来源 ${facts.mode}`,
	);
}

main().catch((err) => {
	console.error("gen-catalog 失败：", err?.message ?? err);
	process.exitCode = 1;
});
