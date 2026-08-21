import { useEffect, useRef, useState, type ReactNode } from "react";
import { createPortal } from "react-dom";
import DialogX from "./DialogX.tsx";

// ─────────────────────────────────────────────────────────────────────────────
// In-app confirm / choose dialogs, replacing window.confirm and window.prompt.
//
// Browser dialogs look like the browser, not like Plutus: they can't be styled, they show the
// hostname ("admin.plutus… says"), they can't offer a dropdown, and window.prompt can only return
// a STRING — which is why "move a till" used to ask for a store NUMBER instead of listing stores.
//
// Usage is imperative on purpose, so a handler keeps reading top-to-bottom:
//     if (!(await ask.confirm({ title: "…", body: <p>…</p>, confirmLabel: "Do it" }))) return;
//     const storeId = await ask.choose({ title: "…", options: stores.map(…) });   // null = cancelled
//
// Mount <AskHost/> once inside the app shell; useAsk() reaches it from anywhere.
//
// ⚠ TWIN FILE: an identical copy lives at
//   Plutus/Frontend/Plutus.Frontend.WebApp/src/Ask.tsx
// The portal and till are separate npm apps and cannot share a package — keep the two files
// byte-for-byte identical (same rule as DataTable.tsx / Barcode39.tsx).
// ─────────────────────────────────────────────────────────────────────────────

export interface ConfirmOptions {
  title: string;
  /** Rich body — lists, warnings, whatever the decision needs. */
  body?: ReactNode;
  confirmLabel?: string;
  cancelLabel?: string;
  /** Style the confirm button as destructive. */
  danger?: boolean;
  /** When set, the confirm button stays disabled until the user types this exactly
   *  (case-insensitive) — for irreversible actions. */
  typeToConfirm?: string;
}

export interface ChooseOption<T extends string | number> {
  value: T;
  label: string;
  /** Optional second line under the label. */
  hint?: string;
}

export interface ChooseOptions<T extends string | number> {
  title: string;
  body?: ReactNode;
  options: ChooseOption<T>[];
  confirmLabel?: string;
  initial?: T;
}

export interface PromptOptions {
  title: string;
  body?: ReactNode;
  label?: string;
  placeholder?: string;
  initial?: string;
  confirmLabel?: string;
  /** Empty input is refused when true (the default). */
  required?: boolean;
}

type Pending =
  | { kind: "confirm"; opts: ConfirmOptions; resolve: (ok: boolean) => void }
  | { kind: "choose"; opts: ChooseOptions<string>; resolve: (v: string | null) => void }
  | { kind: "prompt"; opts: PromptOptions; resolve: (v: string | null) => void };

let enqueue: ((p: Pending) => void) | null = null;

/** The imperative API. Available once <AskHost/> is mounted. */
export const ask = {
  confirm(opts: ConfirmOptions): Promise<boolean> {
    if (!enqueue) return Promise.resolve(window.confirm(opts.title)); // safety net if unmounted
    return new Promise((resolve) => enqueue!({ kind: "confirm", opts, resolve }));
  },
  /** Returns the chosen value, or null if cancelled. Values are strings on the wire — pass
   *  numbers as strings and convert on the way out. */
  choose(opts: ChooseOptions<string>): Promise<string | null> {
    if (!enqueue) return Promise.resolve(null);
    return new Promise((resolve) => enqueue!({ kind: "choose", opts, resolve }));
  },
  /** Free-text input. Returns the text, or null if cancelled. */
  prompt(opts: PromptOptions): Promise<string | null> {
    if (!enqueue) return Promise.resolve(null);
    return new Promise((resolve) => enqueue!({ kind: "prompt", opts, resolve }));
  },
};

/** Mount once, near the root. */
export default function AskHost() {
  const [pending, setPending] = useState<Pending | null>(null);
  const [typed, setTyped] = useState("");
  const [picked, setPicked] = useState("");
  const firstFieldRef = useRef<HTMLInputElement | HTMLSelectElement | null>(null);

  useEffect(() => {
    enqueue = (p) => {
      setTyped(p.kind === "prompt" ? (p.opts.initial ?? "") : "");
      setPicked(p.kind === "choose" ? String(p.opts.initial ?? p.opts.options[0]?.value ?? "") : "");
      setPending(p);
    };
    return () => { enqueue = null; };
  }, []);

  useEffect(() => { if (pending) firstFieldRef.current?.focus(); }, [pending]);

  if (!pending) return null;

  const close = (result: boolean | string | null) => {
    setPending(null);
    if (pending.kind === "confirm") pending.resolve(result === true);
    else pending.resolve(typeof result === "string" ? result : null);
  };

  const armed =
    pending.kind === "confirm"
      ? !pending.opts.typeToConfirm ||
        typed.trim().toLowerCase() === pending.opts.typeToConfirm.trim().toLowerCase()
      : pending.kind === "prompt"
        ? pending.opts.required === false || typed.trim() !== ""
        : picked !== "";

  /** Enter accepts (except where a destructive type-to-confirm demands deliberate input). */
  const onKey = (e: React.KeyboardEvent) => {
    if (e.key === "Escape") { close(null); return; }
    if (e.key === "Enter" && armed && pending.kind !== "confirm") {
      e.preventDefault();
      close(pending.kind === "choose" ? picked : typed.trim());
    }
  };

  // ⚠ Portalled to <body> AND z-indexed above .overlay: an ask is always a QUESTION ABOUT the
  // dialog that raised it, so it must never render beneath one. AskHost is mounted high in the
  // app shell, so plain DOM order put it UNDER any dialog opened by the page below it — the
  // checkout's "Print receipt?" was unreachable and the till looked frozen (2026-08-07).
  return createPortal(
    <div
      className="overlay ask-overlay"
      // Escape cancels and a click on the backdrop cancels — the same affordances the browser
      // dialog gave us for free.
      onClick={(e) => e.target === e.currentTarget && close(null)}
      onKeyDown={onKey}
      role="dialog"
      aria-modal="true"
      aria-label={pending.opts.title}
    >
      <div className="dialog">
        <h3>{pending.opts.title}</h3>
        {/* ⚠ D4 rule 1. Escape, the backdrop and an always-rendered Cancel already closed this — the
            Cancel label falls back to "Cancel" when a caller supplies none — so the ✕ is the intuitive
            exit rather than the only one. */}
        <DialogX onClose={() => close(null)} />
        {pending.opts.body}

        {pending.kind === "choose" && (
          <label>
            {/* the whole point of this component: a real list, not a typed number */}
            <select
              ref={(el) => { firstFieldRef.current = el; }}
              value={picked}
              onChange={(e) => setPicked(e.target.value)}
            >
              {pending.opts.options.map((o) => (
                <option key={String(o.value)} value={String(o.value)}>
                  {o.label}{o.hint ? ` — ${o.hint}` : ""}
                </option>
              ))}
            </select>
          </label>
        )}

        {pending.kind === "prompt" && (
          <label>
            {pending.opts.label}
            <input
              ref={(el) => { firstFieldRef.current = el; }}
              value={typed}
              placeholder={pending.opts.placeholder}
              onChange={(e) => setTyped(e.target.value)}
            />
          </label>
        )}

        {pending.kind === "confirm" && pending.opts.typeToConfirm && (
          <label>
            Type <strong>{pending.opts.typeToConfirm}</strong> to confirm
            <input
              ref={(el) => { firstFieldRef.current = el; }}
              value={typed}
              onChange={(e) => setTyped(e.target.value)}
            />
          </label>
        )}

        <div className="dialog-actions">
          <button className="ghost" onClick={() => close(null)}>
            {(pending.kind === "confirm" && pending.opts.cancelLabel) || "Cancel"}
          </button>
          <button
            className="primary"
            disabled={!armed}
            onClick={() => close(
              pending.kind === "choose" ? picked : pending.kind === "prompt" ? typed.trim() : true,
            )}
          >
            {pending.opts.confirmLabel ??
              (pending.kind === "choose" ? "Continue" : pending.kind === "prompt" ? "Save" : "Confirm")}
          </button>
        </div>
      </div>
    </div>,
    document.body,
  );
}
