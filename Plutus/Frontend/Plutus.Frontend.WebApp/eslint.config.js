import reactHooks from "eslint-plugin-react-hooks";
import tseslint from "typescript-eslint";

/**
 * ⚠⚠ THIS FILE EXISTS FOR ONE DEFECT, AND IT IS A MONEY DEFECT.
 *
 * 2026-08-18: the web till could not take a payment. `CheckoutDialog` called `useMemo` **six lines
 * below a guard clause** (`if (!methods) return …`), so the dialog rendered N hooks on mount and N+1
 * on the next render, and React refused to render at all — *"Minified React error #310: Rendered
 * more hooks than during the previous render."* Matt found it by opening checkout.
 *
 * ⚠ **NOTHING IN THIS PROJECT COULD HAVE CAUGHT IT.** `tsc --noEmit` passes — the code is
 * type-correct. `vite build` passes. All 205 vitest cases pass, because **not one test in this
 * project mounts a component**: every suite tests a pure module, which was a deliberate and mostly
 * good trade, and this is the exact hole it leaves. The bug shipped in 1.10.0, survived a 1.12.0
 * deploy, and was found by a person clicking a button.
 *
 * ⚠ `react-hooks/rules-of-hooks` is the purpose-built detector for precisely this class, and it is a
 * DEV dependency — nothing is added to the bundle. It is the cheapest guard available against the
 * one category of fault this project's test strategy is structurally blind to.
 *
 * Run it with `npm run lint`; `npm run build` runs it FIRST, so a hooks violation now fails the
 * build instead of the counter.
 *
 * ⚠ Deliberately NARROW. This is not a style régime and must not become one: two rules, both
 * correctness, both errors. `exhaustive-deps` is a WARNING on purpose — a missing dep is sometimes
 * the intended behaviour here (the revocation poll's own cadence, for one), and a build that fails
 * on judgement calls gets disabled, at which point the rule that matters goes with it.
 */
export default [
  { ignores: ["dist/**", "node_modules/**", "src/api/types.gen.ts"] },
  {
    files: ["src/**/*.{ts,tsx}"],
    languageOptions: {
      parser: tseslint.parser,
      parserOptions: { ecmaFeatures: { jsx: true }, sourceType: "module" },
    },
    plugins: { "react-hooks": reactHooks },
    rules: {
      // ⚠⚠ THE ONE THAT MATTERS. A hook below a conditional return is React #310 at the counter.
      "react-hooks/rules-of-hooks": "error",
      "react-hooks/exhaustive-deps": "warn",
    },
  },
];
