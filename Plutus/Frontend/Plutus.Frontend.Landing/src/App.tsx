import { useEffect, useState } from "react";
import { appUrls, doorState, type DoorState } from "./api.ts";
import Signup from "./Signup.tsx";

/**
 * The public page.
 *
 * ⚠⚠ EVERY CLAIM BELOW IS GROUNDED IN A ✅/✅ ROW OF `till-design.md` PART A0 — the functional-parity
 * table, which is the register of what a shop can actually do on both tills. Matt asked for copy
 * drafted from the codebase, so nothing here is aspirational: offline selling, the cash-up and Z
 * close, refunds back the way they were paid, and the VAT handling are all built and listed there.
 *
 * ⚠ IT IS STILL DRAFT COPY AND SHOULD BE REWRITTEN IN MATT'S VOICE. What it must not become is a
 * page making claims the register does not support — the constraint, not the prose, is the part
 * worth keeping.
 *
 * ⚠ NO THIRD-PARTY ANYTHING. No analytics, no chat widget, no font CDN, no CAPTCHA. WP-signup §3.2
 * refused a CAPTCHA because "it puts a third-party script on the front door of a payments product",
 * and that argument applies to all of them. The fonts below are system stacks.
 */
export default function App() {
  const [door, setDoor] = useState<DoorState | null>(null);
  const { till, portal } = appUrls();

  useEffect(() => { void doorState().then(setDoor); }, []);

  // ⚠ Deep-linked from the verification email: #verify=<token>. Read once on load.
  const verifyToken = typeof window === "undefined"
    ? null
    : new URLSearchParams(window.location.hash.replace(/^#/, "")).get("verify");

  return (
    <>
      <header className="topbar">
        <a className="brand" href="#top"><Mark /> Plutus</a>
        <nav>
          <a href="#what">What it does</a>
          <a href="#who">Who it's for</a>
          <a href="#pricing">Pricing</a>
        </nav>
        {/* ⚠⚠ LINKS, NEVER A LOGIN FORM (architecture §12c). Hidden rather than broken when the
            URLs are not configured — see appUrls(). */}
        <div className="signin">
          {till && <a className="btn ghost" href={till}>Sign in to your till</a>}
          {portal && <a className="btn ghost" href={portal}>Manage my shop</a>}
        </div>
      </header>

      <main id="top">
        <section className="hero">
          <h1>The till, the stockroom and the VAT return, agreed with each other.</h1>
          <p className="lede">
            Plutus is a point-of-sale system for UK retail. It keeps selling when the internet
            doesn't, it cashes up honestly at the end of the day, and it treats VAT as something to
            get right rather than something to work out later.
          </p>
          <p className="draft-note">
            ⚠ <strong>Draft copy.</strong> Every claim on this page maps to a built-and-tested
            capability, but the wording is a starting point — see <code>WP-landing.md</code> §4.
          </p>
        </section>

        <section id="what">
          <h2>What it does</h2>
          <div className="grid">
            <Card title="Sells when the line drops">
              A sale completes with the network down — scanning, discounts and payment all work
              offline, and the sale syncs when the connection returns. A withdrawn item stays
              withdrawn while offline, so you can't sell what you've pulled from the shelf.
            </Card>
            <Card title="Cashes up, and says what's wrong">
              Open a float, record money in and out, take an X read, close the day with a Z. If the
              drawer is short or over, it tells you which and by how much rather than quietly
              accepting the count. After the day is closed, it refuses to sell.
            </Card>
            <Card title="Refunds the way you were paid">
              Money goes back by the tender it came in on — cash to cash, card to card — and it
              refuses to refund more than was taken. Split payments are refunded across both.
            </Card>
            <Card title="Takes VAT seriously">
              Rates are published once from the back office and every till applies the same ones.
              Zero-rated and exempt stay separate, because they are. Gift cards post no VAT at
              activation, which is what they are: a liability, not a sale.
            </Card>
            <Card title="One catalogue, every till">
              Prices, items and barcodes are set in the back office and pushed out. An item can have
              several barcodes. Withdraw something from sale and it stops selling everywhere.
            </Card>
            <Card title="More than one way to sell">
              A Windows till on the counter, a browser till on anything else, and a webstore channel
              that brings online orders into the same books. Loyalty and gift cards run across them.
            </Card>
          </div>
        </section>

        <section id="who">
          <h2>Who it's for</h2>
          <p>
            Independent and small-chain UK retailers who have outgrown a cash drawer and a
            spreadsheet — shops with real stock, more than one person on the rota, and a VAT return
            to file. It is built for a shop where the till going down means the queue stops.
          </p>
          <p className="muted small">
            ⚠ Draft. If this describes the wrong customer, this is the paragraph to change first —
            everything above is capability, this is positioning.
          </p>
        </section>

        {/* ⚠⚠ PRICING IS A PLACEHOLDER AND MUST BE FILLED BEFORE THE CADDY VHOST EXISTS. Matt chose
            "leave a marked placeholder" over inventing a number or writing "contact us", so this
            section is deliberately unmissable rather than tastefully vague. A price is a commercial
            decision and there is no way to derive one from a codebase. */}
        <section id="pricing" className="placeholder">
          <h2>Pricing</h2>
          <p className="placeholder-flag">⚠ PLACEHOLDER — NOT FOR PUBLICATION</p>
          <p>
            No price has been set. <strong>Fill this in before the site is served publicly</strong>
            — WP-landing §4 notes that a SaaS page with no pricing reads as "call us", which is the
            impression this placeholder is standing in for rather than making.
          </p>
        </section>

        <section id="signup">
          <h2>Get set up</h2>
          {/* ⚠ The form appears only when the door is open. It is never a dead button: the flag
              being off means the endpoint answers 404, and offering a form over that would be worse
              than offering nothing. */}
          <Signup door={door} verifyToken={verifyToken} />
        </section>
      </main>

      <footer>
        <p>
          Plutus is software by Leading Talent.{" "}
          {till && <>Already a customer? <a href={till}>Sign in to your till</a>.</>}
        </p>
        <p className="muted small">
          v{__APP_VERSION__} · built {__BUILD_TIME__}
        </p>
      </footer>
    </>
  );
}

function Card({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <article className="card">
      <h3>{title}</h3>
      <p>{children}</p>
    </article>
  );
}

/** The Plutus mark, inline — ⚠ inline rather than an <img> so the page needs no second request and
 *  renders with images blocked, which is a DoD line. */
function Mark() {
  return (
    <svg width="22" height="22" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
      <circle cx="12" cy="12" r="11" fill="#2c698d" />
      <path d="M8 17V7h4.2a3.4 3.4 0 0 1 0 6.8H10" stroke="#fff" strokeWidth="2"
            fill="none" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}
