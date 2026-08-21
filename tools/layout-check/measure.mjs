import { chromium } from "playwright-core";

// Does the till keep its buttons on screen, and does the BASKET scroll instead of the page?
// Matt, 2026-08-20: "I always need the till buttons to stay on the screen at the bottom. I need the
// items in the till to have a scroll bar if they go off the bottom."
//
// The short viewports are the point: `min-height: 14rem` on .basket-grid was a 224px floor the
// basket could not shrink below, so on a short window it pushed the buttons off regardless of the
// viewport lock. A 1080p check alone would have passed and shipped the fault again.

const browser = await chromium.launch({ executablePath: process.env.CHROME });

const cases = [
  { w: 1920, h: 1080, rows: 8,  label: "1080p, 8 rows (the screenshot)" },
  { w: 1920, h: 1080, rows: 40, label: "1080p, 40 rows" },
  { w: 1366, h: 768,  rows: 40, label: "laptop 768, 40 rows" },
  { w: 1280, h: 620,  rows: 40, label: "SHORT 620, 40 rows" },
  { w: 1280, h: 500,  rows: 40, label: "VERY SHORT 500, 40 rows" },
  { w: 1024, h: 768,  rows: 60, label: "tablet 1024x768, 60 rows" },
];

let failures = 0;

for (const c of cases) {
  const page = await browser.newPage({ viewport: { width: c.w, height: c.h } });
  await page.goto(`file:///tmp/layoutcheck/harness.html?rows=${c.rows}`);

  const m = await page.evaluate(() => {
    const actions = document.getElementById("actions").getBoundingClientRect();
    const basket = document.getElementById("basket");
    const checkout = document.querySelector(".action.checkout").getBoundingClientRect();
    return {
      actionsTop: Math.round(actions.top),
      actionsBottom: Math.round(actions.bottom),
      checkoutBottom: Math.round(checkout.bottom),
      vh: window.innerHeight,
      basketHeight: Math.round(basket.clientHeight),
      basketScrolls: basket.scrollHeight > basket.clientHeight + 1,
      pageScrolls: document.documentElement.scrollHeight > window.innerHeight + 1,
    };
  });

  // The buttons must be fully within the viewport, and the CHECKOUT button specifically.
  const onScreen = m.actionsBottom <= m.vh + 1 && m.actionsTop >= 0 && m.checkoutBottom <= m.vh + 1;
  // With a long basket the BASKET must be what scrolls — not the page.
  const scrollsCorrectly = c.rows < 12 || (m.basketScrolls && !m.pageScrolls);
  const ok = onScreen && scrollsCorrectly;
  if (!ok) failures++;

  console.log(`${ok ? "PASS" : "FAIL"}  ${c.label}`);
  console.log(
    `      buttons ${m.actionsTop}..${m.actionsBottom} of ${m.vh} -> ${onScreen ? "ON SCREEN" : "OFF SCREEN"}` +
    ` | basket ${m.basketHeight}px scrolls=${m.basketScrolls} | PAGE scrolls=${m.pageScrolls}`,
  );

  await page.close();
}

await browser.close();
console.log(failures === 0 ? "\nALL PASS" : `\n${failures} FAILED`);
process.exit(failures === 0 ? 0 : 1);
