/**
 * The ✕ in a dialog's top-right corner.
 *
 * ⚠⚠ WHY THIS EXISTS. Matt, 2026-08-18, about the MAUI till: *"the box it pops has no X to close the
 * box. I know you can click outside of the box to close it but its not intuative"*, then *"add x's to
 * all relevant boxes … so that it is not missed in future"*. The web till had the same gap: every
 * dialog closed by a Cancel button and by clicking the overlay, and **not one of them showed a ✕**.
 *
 * ⚠ PARITY IS THE DEFAULT (CLAUDE.md). MAUI got `CustomViews.DialogHeader`; this is its twin, and the
 * rule both answer to is written down in `Build/till-design.md` **Part D4**.
 *
 * ⚠ It closes, it never submits. Several of these dialogs are `<form>`s, so the button is explicitly
 * `type="button"` — a bare `<button>` inside a form defaults to `type="submit"`, which would turn the
 * close control into "confirm", and on `CheckoutDialog` that means taking money.
 *
 * ⚠ `disabled` while a dialog is busy, for the same reason its Cancel is: closing mid-request leaves
 * the caller waiting on a promise whose UI has gone.
 */
export default function DialogX({ onClose, disabled }: { onClose: () => void; disabled?: boolean }) {
  return (
    <button
      type="button"
      className="dialog-x"
      onClick={onClose}
      disabled={disabled}
      title="Close"
      aria-label="Close"
    >
      ✕
    </button>
  );
}
