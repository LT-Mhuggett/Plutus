import { useEffect, useState } from "react";

/**
 * The till's clock, in the app bar.
 *
 * ⚠⚠ MATT, 2026-08-21, in the same breath as the hour-out bug: *"I need to show a live time on the
 * webtill and MAUI please."* It is not decoration — it is the thing that would have surfaced that bug
 * in a second rather than in a report. A till showing 14:32 next to a sale it says happened at 13:30
 * is a question anybody standing at the counter can ask.
 *
 * ⚠ SECONDS, DELIBERATELY. A clock that shows only `HH:MM` is indistinguishable from a static label
 * for up to a minute, and "is this thing live?" is precisely what somebody is asking when they look
 * at it after a wrong time. This is the one place on the till that ticks.
 *
 * ⚠⚠ **THE TILL'S OWN CLOCK, NOT THE SERVER'S** — decision 2026-08-21, and the caption says so on
 * hover. A shop's till PCs are set to the shop's timezone, and a clock that quietly rendered some
 * other zone would be a second wrong time on the screen instead of a check on the first. A
 * portal-set store timezone is **WP-TZ**, not this.
 *
 * ⚠ THE HOVER CAPTION CARRIES THE TIMEZONE, and that is the diagnostic half. `Europe/London` on the
 * caption of a till reading an hour out tells you immediately whether the fault is the PC or the
 * data — the question that took a morning on 2026-08-21.
 *
 * ⚠ `setTimeout` TO THE NEXT SECOND BOUNDARY, never `setInterval(…, 1000)`. A till tab stays open for
 * days; an interval drifts, and a drifted clock ticks 14:31:59 → 14:32:01 in front of an operator.
 * Re-arming against the wall clock cannot drift, and it self-corrects after the machine sleeps.
 */
export default function TillClock() {
  const [now, setNow] = useState(() => new Date());

  useEffect(() => {
    let timer: number;

    const tick = () => {
      const at = new Date();
      setNow(at);
      // ⚠ +5ms so a timer that fires a hair EARLY does not land back on the same second and show it
      // twice. Rounding down is the common form of this bug and it looks like a frozen clock.
      timer = window.setTimeout(tick, 1005 - at.getMilliseconds());
    };

    tick();
    return () => window.clearTimeout(timer);
  }, []);

  // ⚠ RESOLVED EVERY RENDER, not captured once. A PC whose timezone is corrected mid-shift — which is
  // the fix somebody applies after reading this clock — must be believed without a reload.
  const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;

  return (
    <span
      className="till-clock"
      title={`${now.toLocaleDateString("en-GB", { weekday: "long", day: "numeric", month: "long", year: "numeric" })}\nThis PC's clock · ${zone}`}
    >
      {now.toLocaleTimeString("en-GB")}
    </span>
  );
}
