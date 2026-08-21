# Layout check — does the till keep its buttons on screen?

> **Matt, 2026-08-20:** *"On the webtill and MAUI, I always need the till buttons to stay on the screen
> at the bottom. I need the items in the till to have a scroll bar if they go 'Off the bottom'. I
> thought I had asked for this already."*
>
> He had. It was fixed on 2026-08-20 and the fix sat in an undeployed bundle while he tested 1.26.0.
> **This harness exists so the next person can prove the fix rather than assert it.**

## Why this exists at all

⚠⚠ **Till LAYOUT was invisible to every automated test in this project.** 335 vitest cases, 1526 unit
tests and 26 architecture tests all passed while the Checkout button sat a full screen below the fold.
Nothing in the suite mounts a component, let alone measures one — so "the buttons walk off the bottom"
was only ever discoverable by a person looking at a screen, which is how it survived a round trip.

⚠ **And a 1080p check would NOT have caught it.** With 8 basket lines the old CSS passes: the buttons
fit. The fault needs a long basket, and the *second* fault (`min-height: 14rem` on `.basket-grid`, a
224px floor the basket could not shrink below) needs a SHORT window. Both are in the case list below
for that reason.

## What it measures

A harness page with the till's real DOM nesting (`.shell` → `.page` → `.till-classic` →
`.basket-grid` / `.totals-strip` / `.action-grid`) and the **real `src/index.css`**, driven at six
viewport sizes. Per case it asserts:

1. the action grid — and the **Checkout button specifically** — is fully inside the viewport;
2. with a long basket, **`.basket-grid` is what scrolls and the PAGE does not**.

It tests the CSS chain, not the React app: no login, no server, no test data. That is the whole point —
the fault was in the chain, and the chain can be checked in a second.

## Running it (on the build Mac — the Windows box has no Node)

```bash
ssh -i ~/.ssh/plutus_mac_ed25519 admin@10.1.1.40
export PATH=/opt/homebrew/bin:$PATH

mkdir -p /tmp/layoutcheck && cd /tmp/layoutcheck
npm init -y >/dev/null && npm i --no-audit --no-fund playwright-core@1
npx --yes playwright@1 install chromium

# copy this folder's harness.html + measure.mjs here, plus the CSS under test:
#   cp <repo>/Plutus/Frontend/Plutus.Frontend.WebApp/src/index.css .

CHROME=$(ls -d ~/Library/Caches/ms-playwright/chromium-*/chrome-mac/Chromium.app/Contents/MacOS/Chromium | tail -1) \
  node measure.mjs
```

⚠ **`playwright-core` is installed in /tmp, deliberately NOT added to the web till's
`package.json`.** This repo keeps a minimal-dependency discipline and a browser download is not
something a `npm ci` should drag in. The cost is honest: **this is a tool somebody runs, not a CI
gate.** There is no CI here at all (see till-design C2's root-cause note).

## Proving the harness can actually fail

⚠⚠ **Do this before trusting a PASS.** A layout assertion that cannot detect the fault is worse than
none, because it reads as coverage. Run it against the CSS from before the fix:

```bash
git show <commit-before-the-fix>:Plutus/Frontend/Plutus.Frontend.WebApp/src/index.css > index.css
CHROME=... node measure.mjs     # expect 5 of 6 to FAIL
```

**Recorded result, 2026-08-20** — the same harness, the CSS that was live as web till 1.26.0:

```
PASS  1080p, 8 rows (the screenshot)
      buttons 934..1024 of 1080 -> ON SCREEN | basket 748px scrolls=false | PAGE scrolls=false
FAIL  1080p, 40 rows
      buttons 1911..2001 of 1080 -> OFF SCREEN | basket 1725px scrolls=false | PAGE scrolls=true
FAIL  laptop 768, 40 rows      → buttons 1911..2001 of 768
FAIL  SHORT 620, 40 rows       → buttons 1911..2001 of 620
FAIL  VERY SHORT 500, 40 rows  → buttons 1911..2001 of 500
FAIL  tablet 1024x768, 60 rows → buttons 2758..2848 of 768
5 FAILED
```

…and with the fix (web till 1.27.0), **ALL PASS**, with `.basket-grid` scrolling and the page not.

⚠ Note the first line: **8 rows passes on the broken CSS.** That is why the screenshot looked nearly
right, and it is the case a casual check would have stopped at.

## The MAUI half is NOT covered by this

MAUI's equivalent fix is a star-row `Grid` in `TillView.xaml` (the screen was a vertical
`StackLayout`, which measures children unbounded). **WinUI layout cannot be measured by anything in
this repo** — a MAUI `Page` cannot even be constructed in the test project without a live dispatcher.
`Test Maui.md` **§G62a** is the only check that exists for it, and it is written for a person.
