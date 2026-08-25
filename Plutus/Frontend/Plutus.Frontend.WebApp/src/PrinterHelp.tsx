import { useState } from "react";

/**
 * "How to add or repair a printer" — the walkthrough, in one place.
 *
 * ⚠⚠ WHY IT EXISTS. Matt, 2026-08-25: *"Can you add a 'How to add/repair a printer via the agent'
 * tool tip please. Including images on what to do."* The knowledge was spread across four places —
 * a sentence under the download link, a driver list, a "no agent found" message and the runbook —
 * and none of it was in front of somebody whose printer had just stopped.
 *
 * ⚠⚠ STEP 4 IS THE ONE THAT COST A SHOP ITS PRINTER. On 2026-08-25 the till moved to a new hostname
 * and every agent refused it, because the agent's allowed-origin is an exact string match. A CORS
 * refusal looks EXACTLY like "no agent installed" from the browser, so it is written here as its own
 * step rather than buried in troubleshooting.
 *
 * ⚠ The images are optional and the page is correct without them: each one hides itself if the file
 * is missing, so this ships useful today and gets better when the screenshots are dropped into
 * `public/help/`. A walkthrough that renders broken-image icons is worse than one with none.
 */

/** One illustrated step. ⚠ The image is a progressive enhancement — never a requirement. */
function Shot({ src, alt }: { src: string; alt: string }) {
  const [failed, setFailed] = useState(false);
  if (failed) return null;
  return (
    <figure className="help-shot">
      <img src={src} alt={alt} loading="lazy" onError={() => setFailed(true)} />
      <figcaption className="muted small">{alt}</figcaption>
    </figure>
  );
}

export default function PrinterHelp({ tillOrigin }: { tillOrigin?: string }) {
  // ⚠ The real origin of THIS till, so step 4 can be copied rather than transcribed — the whole
  // point of that step is that an exact string match is easy to get subtly wrong.
  const origin = tillOrigin ?? (typeof window !== "undefined" ? window.location.origin : "");

  return (
    <details className="help-block">
      <summary>🖨 How to add or repair a printer</summary>

      <p className="muted small">
        The till prints through the <strong>Plutus Till Agent</strong> — a small app in the Windows
        system tray, by the clock. The browser cannot reach a printer on its own, so if the agent is
        missing, stopped, or pointed at the wrong address, nothing prints.
      </p>

      <ol className="help-steps">
        <li>
          <strong>Is the agent running?</strong> Look by the clock for the Plutus icon. If it is not
          there, download it from <em>Settings → Hardware → Download the agent</em>, run it, and tick
          <em> start with Windows</em> so it comes back after a reboot.
          <Shot src="/help/printer-1-tray.png" alt="The Plutus Till Agent icon in the Windows system tray" />
        </li>

        <li>
          <strong>Pick the printer.</strong> Right-click the tray icon → <em>Settings</em> → choose
          your receipt printer in <em>Printer</em>. If it is not listed, Windows does not have a
          driver for it yet — install the maker's driver first (links are under Hardware), then
          reopen this list.
          <Shot src="/help/printer-2-choose.png" alt="Choosing the receipt printer in the agent's Settings window" />
        </li>

        <li>
          <strong>Pair it with this till.</strong> Copy the agent's <em>pairing token</em> and paste
          it into <em>Settings → Hardware → Pairing token</em> here. The token is what stops another
          page in the same browser driving your till's drawer.
          <Shot src="/help/printer-3-token.png" alt="The pairing token in the agent, and where it goes in the till" />
        </li>

        <li>
          <strong>⚠ Check the address the agent will accept.</strong> In the agent's Settings, the
          box <em>"Till address allowed to connect"</em> must match this till exactly:
          <div className="help-origin"><code>{origin}</code></div>
          If it does not, the agent silently refuses the till and the browser reports it as{" "}
          <em>"no agent found"</em> — the two look identical from here. It accepts a comma-separated
          list if you are moving between addresses.
          <Shot src="/help/printer-4-origin.png" alt="The 'Till address allowed to connect' box in the agent's Settings" />
        </li>

        <li>
          <strong>Prove it.</strong> <em>Settings → Hardware → Test print</em>, then{" "}
          <em>Open drawer</em>. A receipt and a clunk means it is done.
        </li>
      </ol>

      <h4 className="help-h">If it was working and stopped</h4>
      <ul className="help-steps">
        <li>
          <strong>"No agent found"</strong> — three different faults share this message: the agent is
          not running, the pairing token is wrong, or <strong>the till's address is not in the
          agent's allowed list</strong> (step 4). Check them in that order; the third is the one
          people miss, and it is what happens after the till's web address changes.
        </li>
        <li>
          <strong>Prints blank, or the totals are missing</strong> — the printer is set to the wrong
          paper size. In the Windows printer's own properties, set the receipt roll width
          (72mm on a Star TSP143) and the paper type to <em>Receipt</em>.
        </li>
        <li>
          <strong>Nothing at all, and the tray icon is grey</strong> — the agent has stopped. Start
          it from the Start menu; if it keeps stopping, reboot the PC before anything else.
        </li>
        <li>
          <strong>The drawer will not open but receipts print</strong> — the drawer is kicked through
          the printer, so this is nearly always the cable into the back of the printer rather than
          anything on screen.
        </li>
      </ul>

      <p className="muted small">
        ⚠ If none of that helps, use <strong>Help &amp; support</strong> (the ❓ in the top bar) and
        say which step you got to — it reaches somebody who can see this till's agent status.
      </p>
    </details>
  );
}
