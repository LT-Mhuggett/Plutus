import { useMemo, useState, type ReactNode } from "react";

// ─────────────────────────────────────────────────────────────────────────────
// Standard table for Plutus (see Build/table-standard.md). Sortable columns,
// search box, page-size 25/50/100, pagination — the "Plutus Portal Stock page"
// UX made reusable. TWO modes:
//   • client mode (default): pass all `rows`; the table sorts/searches/paginates.
//   • server mode: pass `server` (total/skip/take/search + callbacks); the parent
//     owns the data window (use whenever the endpoint supports skip/take/search).
//     Header sort in server mode orders the CURRENT page only (matches the legacy
//     Stock page); wire server sort in the parent if global ordering is needed.
//
// ⚠ TWIN FILE: an identical copy lives at
//   Plutus/Frontend/Plutus.Frontend.WebApp/src/DataTable.tsx
// The portal and till are separate npm apps and cannot share a package — keep the
// two files byte-for-byte identical. Change one → change the other.
// ─────────────────────────────────────────────────────────────────────────────

export interface Column<T> {
  key: string;
  label: string;
  numeric?: boolean;                        // right-align (adds class "num")
  sortable?: boolean;                        // default true
  sort?: (row: T) => string | number;        // client sort value; defaults to row[key]
  render?: (row: T) => ReactNode;            // cell content; defaults to String(row[key])
}

export interface ServerPaging {
  total: number;
  skip: number;
  take: number;
  search: string;
  onSearch: (s: string) => void;
  onPage: (skip: number, take: number) => void;
}

export const PAGE_SIZES = [25, 50, 100];

function cmp(a: string | number, b: string | number): number {
  if (typeof a === "number" && typeof b === "number") return a - b;
  return String(a ?? "").localeCompare(String(b ?? ""), undefined, { numeric: true, sensitivity: "base" });
}

export default function DataTable<T>({
  columns, rows, getKey, search, server, emptyText = "No rows.", rowActions,
  initialSortKey, initialSortDir = "asc", searchPlaceholder = "Search…",
}: {
  columns: Column<T>[];
  rows: T[];
  getKey: (row: T) => string;
  /** client-mode search haystack; omit to hide the search box in client mode */
  search?: (row: T) => string;
  /** presence switches to server mode (parent owns data/paging/search) */
  server?: ServerPaging;
  emptyText?: string;
  rowActions?: (row: T) => ReactNode;
  initialSortKey?: string;
  initialSortDir?: "asc" | "desc";
  searchPlaceholder?: string;
}) {
  const isServer = !!server;
  const [sortKey, setSortKey] = useState<string | undefined>(initialSortKey);
  const [sortDir, setSortDir] = useState<"asc" | "desc">(initialSortDir);
  const [query, setQuery] = useState("");             // client-mode search
  const [page, setPage] = useState(0);                // client-mode page index
  const [size, setSize] = useState(PAGE_SIZES[0]);    // client-mode page size

  const sortRows = (arr: T[]): T[] => {
    if (!sortKey) return arr;
    const col = columns.find((c) => c.key === sortKey);
    if (!col) return arr;
    const val = (r: T) => (col.sort ? col.sort(r) : ((r as Record<string, unknown>)[col.key] as string | number));
    const s = [...arr].sort((a, b) => cmp(val(a), val(b)));
    return sortDir === "asc" ? s : s.reverse();
  };

  const filtered = useMemo(() => {
    if (isServer || !search || !query.trim()) return rows;
    const q = query.trim().toLowerCase();
    return rows.filter((r) => search(r).toLowerCase().includes(q));
  }, [rows, query, search, isServer]);

  const take = isServer ? server!.take : size;
  const skip = isServer ? server!.skip : page * size;
  const total = isServer ? server!.total : filtered.length;
  const pageRows = isServer ? sortRows(rows) : sortRows(filtered).slice(skip, skip + size);

  const setSort = (key: string) => {
    if (sortKey === key) setSortDir((d) => (d === "asc" ? "desc" : "asc"));
    else { setSortKey(key); setSortDir("asc"); }
  };
  const changeSize = (n: number) => { if (isServer) server!.onPage(0, n); else { setSize(n); setPage(0); } };
  const gotoSkip = (s: number) => { if (isServer) server!.onPage(Math.max(0, s), take); else setPage(Math.max(0, Math.floor(s / size))); };
  const onSearchChange = (v: string) => { if (isServer) server!.onSearch(v); else { setQuery(v); setPage(0); } };

  const showSearch = isServer || !!search;
  const from = total === 0 ? 0 : skip + 1;
  const to = Math.min(skip + take, total);
  const cols = columns.length + (rowActions ? 1 : 0);

  return (
    <div className="datatable">
      {(showSearch || true) && (
        <div className="toolbar" style={{ alignItems: "center" }}>
          {showSearch && (
            <input value={isServer ? server!.search : query} placeholder={searchPlaceholder}
              onChange={(e) => onSearchChange(e.target.value)} />
          )}
          <span className="grow" />
          <label className="muted small">Show{" "}
            <select value={take} onChange={(e) => changeSize(Number(e.target.value))}>
              {PAGE_SIZES.map((n) => <option key={n} value={n}>{n}</option>)}
            </select>
          </label>
        </div>
      )}
      <table>
        <thead>
          <tr>
            {columns.map((c) => {
              const sortable = c.sortable !== false;
              const active = sortKey === c.key;
              return sortable ? (
                <th key={c.key} className={`sortable${c.numeric ? " num" : ""}`} onClick={() => setSort(c.key)} title="Sort">
                  {c.label}<span className="sort-ind">{active ? (sortDir === "asc" ? " ▲" : " ▼") : " ⇅"}</span>
                </th>
              ) : (
                <th key={c.key} className={c.numeric ? "num" : undefined}>{c.label}</th>
              );
            })}
            {rowActions && <th />}
          </tr>
        </thead>
        <tbody>
          {pageRows.map((r) => (
            <tr key={getKey(r)}>
              {columns.map((c) => (
                <td key={c.key} className={c.numeric ? "num" : undefined}>
                  {c.render ? c.render(r) : String((r as Record<string, unknown>)[c.key] ?? "")}
                </td>
              ))}
              {rowActions && <td>{rowActions(r)}</td>}
            </tr>
          ))}
          {pageRows.length === 0 && <tr><td colSpan={cols} className="muted">{emptyText}</td></tr>}
        </tbody>
      </table>
      <div className="toolbar" style={{ alignItems: "center" }}>
        <span className="muted small">{from}–{to} of {total}</span>
        <span className="grow" />
        <button className="ghost small" disabled={skip <= 0} onClick={() => gotoSkip(skip - take)}>Prev</button>
        <button className="ghost small" disabled={to >= total} onClick={() => gotoSkip(skip + take)}>Next</button>
      </div>
    </div>
  );
}
