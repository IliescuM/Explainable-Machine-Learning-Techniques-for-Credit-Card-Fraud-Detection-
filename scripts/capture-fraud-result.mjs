import { chromium } from "playwright";

const url = process.argv[2] || "http://localhost:5246/fraud";
const out = process.argv[3] || "figures/ui-fraud-scored.png";

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
await page.goto(url, { waitUntil: "networkidle" });
await page.getByRole("button", { name: "Score" }).click();
await new Promise((r) => setTimeout(r, 2500));
await page.screenshot({ path: out, fullPage: true });
await browser.close();
