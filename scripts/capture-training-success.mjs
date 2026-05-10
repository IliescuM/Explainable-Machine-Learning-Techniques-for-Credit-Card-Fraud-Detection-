import { chromium } from "playwright";

const url = process.argv[2] || "http://localhost:5246/training";
const out = process.argv[3] || "figures/ui-training-success.png";

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1400, height: 980 } });
await page.goto(url, { waitUntil: "networkidle" });
await page.waitForSelector("#trainingDataset");

await page.selectOption("#trainingDataset", "2"); // CreditcardLocalCsv
await page.selectOption("#trainingModel", "2"); // FastTree gradient boost

await page.getByRole("button", { name: "Start training" }).click();
await page.waitForFunction(
  () => {
    const t = document.body?.innerText ?? "";
    return t.includes("ML.NET training finished") && t.includes("ROC-AUC");
  },
  { timeout: 1_800_000 }
);
await page.waitForTimeout(800);
await page.screenshot({ path: out, fullPage: true });
await browser.close();
