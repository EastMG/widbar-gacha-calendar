// 抓一份真实的 latest.json 存成 data/sample-feed.json（首次离线兜底用）。
//
// 用法：node tools/fetch-sample-feed.mjs
// 说明：抓完把 generatedAt 一并保留；仓库里提交它，供没有网络时首屏展示。
//       只在明确要更新示例数据时跑，不要放进 CI。

import { writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const FEED_URL = "https://eastmg.github.io/gacha-calendar-app/data/latest.json";

const here = dirname(fileURLToPath(import.meta.url));
const outPath = join(here, "..", "data", "sample-feed.json");

const response = await fetch(FEED_URL, {
	headers: {
		"user-agent": "widbar-gacha-calendar/sample-feed (+https://github.com/EastMG/widbar-gacha-calendar)",
	},
});
if (!response.ok) {
	console.error(`抓取失败：HTTP ${response.status} ${response.statusText}`);
	process.exit(1);
}

const text = await response.text();
JSON.parse(text); // 坏 JSON 直接失败，避免把垃圾写进仓库

writeFileSync(outPath, text.endsWith("\n") ? text : text + "\n", "utf8");
console.log(`已写入 ${outPath}（${Buffer.byteLength(text)} 字节）`);
