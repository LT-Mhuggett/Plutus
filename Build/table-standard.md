# Plutus table standard

Every data table in **all three surfaces** (web till, management portal, operator console)
must behave the same way. This is enforced by a shared component, `DataTable`.

## The rules

A standard table is:

1. **Orderable** — click a column header to sort (▲/▼, ⇅ when unsorted). Numeric-aware
   (`"Store 2" < "Store 10"`).
2. **Searchable** — a single search box filters the rows.
3. **Page-sized** — a "Show 25 / 50 / 100" selector (default 25).
4. **Paginated** — Prev / Next with an "X–Y of N" counter.

Cell conventions: numeric cells use `className="num"` (right-aligned); ids/barcodes use
`mono small`; empty-state and secondary text use `muted`. Money is always formatted with the
app's `gbp()` helper (pence in, `£` out).

## The component

`DataTable<T>` lives in each frontend's `src/DataTable.tsx`. The portal and till are separate
npm apps and can't share a package, so the file is a **byte-identical twin** in both:

- `Plutus/Frontend/Plutus.Frontend.Portal/src/DataTable.tsx`
- `Plutus/Frontend/Plutus.Frontend.WebApp/src/DataTable.tsx`

**Change one, change the other** (there's a header comment in the file saying so). If they ever
drift, copy the portal one over the till one.

### Two modes

**Client mode** (default) — for datasets you already hold in full (roughly ≤ 500 rows).
The table sorts, searches and paginates in-component:

```tsx
<DataTable
  columns={[
    { key: "name", label: "Name" },
    { key: "qty", label: "Qty", numeric: true },
    { key: "gross", label: "Gross", numeric: true, render: (r) => gbp(r.grossPence), sort: (r) => r.grossPence },
  ]}
  rows={rows}
  getKey={(r) => r.id}
  search={(r) => `${r.name}`}            // omit to hide the search box
  initialSortKey="gross" initialSortDir="desc"
  rowActions={(r) => <button className="ghost small" onClick={() => open(r)}>Edit</button>}
/>
```

**Server mode** — for large/paged endpoints (use whenever the API supports `skip`/`take`/`search`,
e.g. `/api/v1/stock/levels`). The parent owns the data window; the table just renders the controls
and calls back:

```tsx
<DataTable
  columns={cols}
  rows={pageRows}
  getKey={(r) => r.itemIdOne}
  server={{
    total, skip, take, search,
    onSearch: (s) => { setSearch(s); setSkip(0); },
    onPage: (skip, take) => { setSkip(skip); setTake(take); },
  }}
/>
```

In server mode, header-click sorting orders the **current page only** (matching the legacy Stock
page). If you need global ordering, add an `orderBy` query param in the parent and re-fetch.

### `Column<T>` fields

| field | meaning |
|---|---|
| `key` | unique column id; also the default row field to read/sort |
| `label` | header text |
| `numeric` | right-align the header + cells |
| `sortable` | default `true`; set `false` to disable sorting on that column |
| `sort` | client-mode sort value (defaults to `row[key]`) — use for computed/formatted columns |
| `render` | cell content (defaults to `String(row[key])`) |

`rowActions(row)` adds a trailing actions cell (Edit/Open buttons etc.).

## Adopting it

- **New tables MUST use `DataTable`.** No hand-rolled `<table>` + bespoke pagination.
- **Existing tables** migrate opportunistically — when a work package touches a page, convert its
  table as part of that change rather than in a separate big-bang sweep.
