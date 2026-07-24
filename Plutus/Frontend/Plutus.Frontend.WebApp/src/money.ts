// All till arithmetic is done in integer pence to avoid float drift; conversion
// to/from the API's decimal pounds happens only at the boundary.

export const toPence = (pounds: number): number => Math.round(pounds * 100);
export const toPounds = (pence: number): number => pence / 100;

const fmt = new Intl.NumberFormat("en-GB", { style: "currency", currency: "GBP" });
export const gbp = (pence: number): string => fmt.format(pence / 100);

/** Parse operator input ("1.50", "£1.50", "150p") to pence; null when invalid. */
export function parsePence(input: string): number | null {
  const cleaned = input.trim().replace(/^£/, "");
  if (!/^\d+(\.\d{1,2})?$/.test(cleaned)) return null;
  return Math.round(parseFloat(cleaned) * 100);
}
