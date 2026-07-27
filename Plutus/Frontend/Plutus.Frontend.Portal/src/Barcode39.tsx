// WP11.2: a hand-rolled Code 39 ("3 of 9") barcode as inline SVG — no library (plan rule).
// Code 39 covers 0-9, A-Z, '-' and a few symbols, which is exactly the charset of an uppercased
// UUID saleId. Rendered on receipts so a return can be done by scanning the receipt.

// Each glyph is 9 elements (bar,space,bar,…,bar); '1' = wide, '0' = narrow. Exactly 3 are wide.
const PATTERNS: Record<string, string> = {
  "0": "000110100", "1": "100100001", "2": "001100001", "3": "101100000", "4": "000110001",
  "5": "100110000", "6": "001110000", "7": "000100101", "8": "100100100", "9": "001100100",
  A: "100001001", B: "001001001", C: "101001000", D: "000011001", E: "100011000", F: "001011000",
  G: "000001101", H: "100001100", I: "001001100", J: "000011100", K: "100000011", L: "001000011",
  M: "101000010", N: "000010011", O: "100010010", P: "001010010", Q: "000000111", R: "100000110",
  S: "001000110", T: "000010110", U: "110000001", V: "011000001", W: "111000000", X: "010010001",
  Y: "110010000", Z: "011010000", "-": "010000101", ".": "110000100", " ": "011000100",
  "*": "010010100", // start/stop sentinel
};

/** Sanitise to the Code 39 charset (uppercase; drop anything unsupported). */
function encodable(value: string): string {
  return value.toUpperCase().split("").filter((c) => c in PATTERNS && c !== "*").join("");
}

export default function Barcode39({
  value, height = 46, narrow = 2, ratio = 3, showText = true,
}: { value: string; height?: number; narrow?: number; ratio?: number; showText?: boolean }) {
  const text = encodable(value);
  if (!text) return null;
  const wide = narrow * ratio;
  const framed = `*${text}*`;

  const rects: { x: number; w: number }[] = [];
  let x = 0;
  for (const ch of framed) {
    const pattern = PATTERNS[ch];
    for (let i = 0; i < pattern.length; i++) {
      const w = pattern[i] === "1" ? wide : narrow;
      if (i % 2 === 0) rects.push({ x, w }); // even element = bar
      x += w;
    }
    x += narrow; // inter-character narrow gap
  }
  const width = x;

  return (
    <svg
      className="barcode"
      viewBox={`0 0 ${width} ${height + (showText ? 14 : 0)}`}
      width={width}
      height={height + (showText ? 14 : 0)}
      role="img"
      aria-label={`Barcode ${text}`}
    >
      {rects.map((r, i) => (
        <rect key={i} x={r.x} y={0} width={r.w} height={height} fill="#000" />
      ))}
      {showText && (
        <text x={width / 2} y={height + 11} textAnchor="middle" fontSize="10" fontFamily="monospace">
          {text}
        </text>
      )}
    </svg>
  );
}
