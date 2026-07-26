import { useMemo, useState } from "react";

/** Click-to-sort for any table. Numbers sort numerically; strings numeric-aware
 *  (so "Store 2" < "Store 10"). Pass an initial key/dir for a default order. */
export function useSort<T>(rows: T[], initialKey?: string, initialDir: "asc" | "desc" = "asc") {
  const [key, setKey] = useState<string | null>(initialKey ?? null);
  const [dir, setDir] = useState<"asc" | "desc">(initialDir);

  const sorted = useMemo(() => {
    if (!key) return rows;
    const copy = [...rows];
    copy.sort((a, b) => {
      const av = (a as Record<string, unknown>)[key];
      const bv = (b as Record<string, unknown>)[key];
      let cmp: number;
      if (typeof av === "number" && typeof bv === "number") cmp = av - bv;
      else cmp = String(av ?? "").localeCompare(String(bv ?? ""), undefined, { numeric: true, sensitivity: "base" });
      return dir === "asc" ? cmp : -cmp;
    });
    return copy;
  }, [rows, key, dir]);

  const onSort = (k: string) => {
    if (key === k) setDir((d) => (d === "asc" ? "desc" : "asc"));
    else { setKey(k); setDir("asc"); }
  };

  return { sorted, sortKey: key, sortDir: dir, onSort };
}

/** A sortable <th>. `k` is the row field to sort by; `num` right-aligns. */
export function SortTh({ label, k, sortKey, sortDir, onSort, num }: {
  label: string; k: string; sortKey: string | null; sortDir: "asc" | "desc";
  onSort: (k: string) => void; num?: boolean;
}) {
  const active = sortKey === k;
  return (
    <th className={`sortable${num ? " num" : ""}`} onClick={() => onSort(k)} title="Sort">
      {label}<span className="sort-ind">{active ? (sortDir === "asc" ? " ▲" : " ▼") : " ⇅"}</span>
    </th>
  );
}
