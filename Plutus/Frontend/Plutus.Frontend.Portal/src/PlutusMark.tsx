// The Plutus brand mark: a "P" drawn as a currency symbol (barred stem).
// Same 64-unit geometry as public/icon.svg — keep the two in step if either changes.
// Fills with currentColor so it takes the accent of whatever it sits in; the tab/PWA
// icon carries the blue tile. (Kept in sync by hand with the till's copy — the two
// frontends share no package.)
export function PlutusMark({ size = 26 }: { size?: number }) {
  return (
    <svg className="plutus-mark" width={size} height={size} viewBox="0 0 64 64" aria-hidden="true" focusable="false">
      <g fill="currentColor">
        <path d="M32.5 9.5a14 14 0 0 1 0 28v-7a7 7 0 0 0 0-14z" />
        <rect x="24.5" y="9.5" width="8" height="45" />
        <rect x="17.5" y="43" width="22" height="6" />
      </g>
    </svg>
  );
}
