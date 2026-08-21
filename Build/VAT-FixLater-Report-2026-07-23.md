# VAT — deferred fixes report

**Date:** 2026-07-23 · **Status:** guardrails LIVE; the items below are legacy damage, deliberately NOT auto-repaired (owner decision).
**Live view:** Reporting → VAT shows this list with a warning banner — it shrinks as items are corrected.
⚠ **The endpoint moved 2026-07-27**: it is `GET /api/v1/reports/vat-integrity`, **not** the legacy
`/api/Sale/VatIntegrity` this line used to name.

> ### ✅ RE-VERIFIED AGAINST THE LIVE SERVER 2026-08-20 — this report is CURRENT, not history
>
> Matt asked whether this document was stale enough to archive. **It is not.** The live endpoint answers
> **`offBandCount: 47`** — the same 47 items, the same 20-at-20% / 27-zero-rated split, and the same rows
> in the same order. **Nothing on this list has been corrected in 28 days**, so it is a live worklist
> rather than a record of finished work, and it stays out of `archive/`.
>
> ⚠⚠ **ONE THING WAS WRONG AND IS NOW FIXED: THE BAND COLUMN SAID `Exempt` FOR 27 ITEMS THAT ARE
> ACTUALLY `Zero rated (books)`.** Verified by intersecting ids with the live payload — identical sets,
> all 27. This mattered more than a label: **zero-rated and exempt are different things** (both 0%, but
> they behave differently on a VAT return and the platform deliberately keeps them apart). Anybody
> correcting an item from the old table would have set **Exempt on a book that should be Zero rated**,
> and every total would still have balanced — the silent failure this whole report exists to prevent.
>
> ⚠ **The ROW COUNT here is a 2026-07-23 snapshot and the live view is the authority.** It happens to
> agree today. Do not assume it still will — check the endpoint before quoting a number from this page.
> ⚠ **And the catalogue was dropped and reloaded from the 19_08 backup on 2026-08-20** (NatApp full
> replace, §8) — these 47 survived that, which means they are in the *legacy source data*, not damage
> the platform introduced.

## What is already protected (done 2026-07-23)
- API rejects any new/edited item whose `|Price − ExPrice × band rate| > 2p` (400 + clear message; `isSync` writes exempt so legacy tills can't dead-letter).
- The webapp item editor derives ex-VAT from the band, so it cannot produce these.
- Reporting → VAT banner surfaces the live count so regressions are visible immediately.

## To fix later

### 1. The 47 off-band catalogue items (worst first)
Correct each item's ex-VAT price (usually `Price ÷ band rate`), or its band, via any item editor — the webapp editor recomputes correctly on save.

| Barcode / id | Name | Band | Price £ | Stored ex £ | Price implied by band £ |
|---|---|---|---:|---:|---:|
| 9781974732517 | ELUSIVE SAMURAI GN VOL 01 | Zero rated (books) | 7.99 | 799.00 | 799.00 |
| 5011921161874 | CITADEL TOOLS: KNIFE | 20% | 19.99 | 116.66 | 139.99 |
| 5011921252909 | CITIES OF SIGMAR: CANNONADE COGFORT | Zero rated (books) | 125.00 | 104.17 | 104.17 |
| 9781302919467 | FEARLESS TP | Zero rated (books) | 1.00 | 14.50 | 14.50 |
| 5011921156900 | ORKS: MEK GUN | 20% | 36.99 | 20.83 | 25.00 |
| 9781984856449 | DUNGEONS & TOMBS YOUNG ADVENTURERS GUIDE D&D HC | Zero rated (books) | 1.00 | 11.99 | 11.99 |
| 9781302962913 | EARTH X TP | Zero rated (books) | 29.99 | 39.99 | 39.99 |
| 9781302964009 | PUNISHER RED BAND BRAIN BLEED TP | Zero rated (books) | 15.99 | 25.99 | 25.99 |
| 9781638494959 | DUNGEON CRAWLER CARL TP VOL 01 CVR B | Zero rated (books) | 17.99 | 27.90 | 27.90 |
| 9781974749485 | JUJUTSU KAISEN GN VOL 24 | Zero rated (books) | 8.99 | 1.00 | 1.00 |
| 5011921139101 | NECRONS: FLAYED ONES | Zero rated (books) | 44.00 | 36.66 | 36.66 |
| 9781837865635 | BIG ASS SWORD HC | Zero rated (books) | 11.69 | 16.99 | 16.99 |
| 5010996143754 | MARVEL LEGENDS 6IN X-MEN 97 MARVEL'S ROGUE AF | 20% | 24.99 | 24.99 | 29.99 |
| 5010996282972 | MARVEL LEGENDS 6IN FF MOVIE MR FANTASTIC AF | Zero rated (books) | 29.99 | 24.99 | 24.99 |
| 5010996328199 | MARVEL LEGENDS 6IN EMMA FROST AF | Zero rated (books) | 29.99 | 24.99 | 24.99 |
| 5010993979356 | GI JOE CLASSIFIED SER TF 6IN PYTHON PATROL OFFICER | Zero rated (books) | 29.99 | 25.04 | 25.04 |
| 4573102687050 | RG GUNDAM SHINING 1/144 | 20% | 39.99 | 29.99 | 35.99 |
| 5010993790531 | MARVEL LEGENDS 6IN X-MEN MOIRA MACTAGGERT AF | Zero rated (books) | 22.99 | 19.04 | 19.04 |
| 5010993697083 | MARVEL DEADPOOL LEGENDS 6IN SHIKLAH AF | Zero rated (books) | 22.99 | 19.14 | 19.14 |
| 5011921200474 | SPACE MARINES: STERNGUARD VETERAN SQUAD | 20% | 39.99 | 30.39 | 36.47 |
| 5010993648627 | TRANSFORMERS GEN WFCE DLX AIRWAVE AF | Zero rated (books) | 19.99 | 16.92 | 16.92 |
| 5010993648658 | TRANSFORMERS GEN WFCE DLX SMOKESCREEN AF | Zero rated (books) | 19.99 | 17.00 | 17.00 |
| 5011921188666 | REAVER TITAN W/MELTA CANNON & CHAINFIST | 20% | 39.99 | 31.24 | 37.49 |
| 889698758673 | POP TELEVISION INVINCIBLE INVINCIBLE VIN FIG | 20% | 12.99 | 11.69 | 14.03 |
| 9781799507864 | BATMAN THE DARK KNIGHT RETURNS TP COMPACT COMICS | Zero rated (books) | 8.99 | 9.99 | 9.99 |
| 759606211784 | KIDPOOL SPIDER-BOY #1 | Zero rated (books) | 6.10 | 5.10 | 5.10 |
| 761941377438 | SWORD OF AZRAEL DARK KNIGHT OF SOUL #1 | Zero rated (books) | 3.55 | 2.55 | 2.55 |
| 889698877985 | POCKET POPERS CHUCKY TIFFANY FIG | 20% | 8.99 | 6.66 | 7.99 |
| 5010996246158 | MARVEL LEGENDS 6IN GHOST RIDER W/ MOTORCYCLE | 20% | 69.99 | 58.88 | 70.66 |
| 5011921181377 | DARK ANGELS LION EL'JOHNSON | 20% | 44.49 | 37.49 | 44.99 |
| 5011921169917 | T'AU EMPIRE: HAMMERHEAD GUNSHIP | 20% | 47.49 | 39.83 | 47.80 |
| 759606211586 | MARVEL HOLIDAY TALES TO ASTONISH #1 | Zero rated (books) | 5.25 | 5.35 | 5.35 |
| 5011921178186 | Chaos Space Marines: Forgefiend | 20% | 54.49 | 45.33 | 54.40 |
| 9781974719822 | LEGEND OF ZELDA TWILIGHT PRINCESS GN VOL 08 | Zero rated (books) | 7.99 | 7.90 | 7.90 |
| 9781974762330 | MINECRAFT: THE MANGA GN VOL 05 | Zero rated (books) | 8.99 | 8.90 | 8.90 |
| 9781632158956 | PAPER GIRLS TP VOL 02 | Zero rated (books) | 11.99 | 11.90 | 11.90 |
| 9781779508010 | BATMAN (2020) TP VOL 01 THEIR DARK DESIGNS | Zero rated (books) | 22.90 | 22.99 | 22.99 |
| 9781779516732 | ROBIN 2021 TP VOL 02 I AM ROBIN | Zero rated (books) | 16.90 | 16.99 | 16.99 |
| 9781786185655 | THISTLEBONE BOOK 2 POISONED ROOTS HC | Zero rated (books) | 14.99 | 14.90 | 14.90 |
| 5011921138814 | COMBAT PATROL: DEATH GUARD | 20% | 99.99 | 83.25 | 99.90 |
| 5011921251216 | CHAPTER APPROVED MISSION PACK (ENG) | 20% | 20.00 | 16.60 | 19.92 |
| 889698839945 | POP SUPER MARVEL RIVALS GALACTA FIG | 20% | 24.99 | 20.88 | 25.06 |
| 4012927945940 | YU GI OH 2022 TIN PHARAOHS GODS | 20% | 18.74 | 15.66 | 18.79 |
| 787926170979 | DC MULTIVERSE COLLECTOR 7IN WV5 #14 SGT ROCK | 20% | 34.99 | 29.19 | 35.03 |
| 5011921247882 | SPACE WOLVES: WOLF GUARD TERMINATORS | 20% | 42.49 | 35.38 | 42.46 |
| 5011921143030 | COMBAT PATROL: THOUSAND SONS | 20% | 99.99 | 83.35 | 100.02 |
| 5011921143023 | COMBAT PATROL: GREY KNIGHTS | 20% | 99.99 | 83.35 | 100.02 |

### 2. NatApp regressions (code fixes, from VAT-Investigation plan §5.3)
- `Transaction_Discounts.DiscountRate` is written as **0** in the current NatApp era (2019 data has real rates) — find the checkout write site and restore the rate.
- NatApp item entry/edit free-types `ExPrice` — derive it from the band like the webapp does.
- NatApp Adjust dialog lets Price/PriceExTax diverge from the band ratio — scale proportionally.

### 3. Bookkeeping note
Historic headline VAT (e.g. H1-2026's £2,078.23) contains the noise from these items. When the 47 are repaired, past figures do NOT change (sales store prices as sold) — if a corrected historic VAT figure is ever needed, recompute per VAT-Investigation plan §5.2 step 1.
