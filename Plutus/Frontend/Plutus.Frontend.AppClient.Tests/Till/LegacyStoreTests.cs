using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Plutus.Frontend.AppClient.Tests.Till
{
    /// <summary>
    /// **Nothing may dereference the legacy store without checking it first — cutover step 21.**
    ///
    /// ⚠⚠ `App.GetViewModel().Store` IS NULL ON EVERY MODERN TILL NOW. `LoginViewModel.EnsureStoreAsync`
    /// used to write a legacy `StoreModel` row at sign-in purely so it would not be; it was deleted on
    /// 2026-08-23 and the platform has been the source of truth for store details since step 20.
    ///
    /// ⚠ THE ONLY THING THAT EVER MADE THE DELETION UNSAFE WAS UNGUARDED DEREFERENCES, and the register
    /// warned about exactly one wrong fix for them: *"Do NOT unblock this by null-coalescing to 0 —
    /// that writes stock rows against store 0, which is a silent data change wearing a null-fix
    /// disguise."* A guard that refuses is right; a default that invents a store id is not.
    ///
    /// ⚠ AND THE HISTORY IS WHY THIS IS PINNED RATHER THAN TRUSTED. The blocking row was wrong twice —
    /// once claiming the method threw on every sign-in when its body cannot throw at all, once costing
    /// the deletion at half a day when it would have NullReferenced two screens. A comment did not stop
    /// either; a test can.
    ///
    /// ⚠ Source text, because the MAUI app does not build on a test host and no screen here has
    /// automated coverage. Crude, and the only thing available.
    /// </summary>
    public class LegacyStoreTests
    {
        private static string RepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (dir is not null && !Directory.Exists(Path.Combine(dir, ".git")))
                dir = Path.GetDirectoryName(dir);
            Assert.NotNull(dir);
            return dir!;
        }

        private static string TillRoot() => Path.Combine(
            RepoRoot(), "Plutus", "Frontend", "Plutus.Frontend.AppClient");

        /// <summary>`App.GetViewModel().Store.<something>` — a dereference with nothing in between.</summary>
        private static readonly Regex Dereference = new(
            @"App\.GetViewModel\(\)\.Store\.", RegexOptions.Compiled);

        /// <summary>`?? 0`, `?.Id ?? 0`, `Store?.Id ?? 0` — inventing a store id.</summary>
        private static readonly Regex InventsAnId = new(
            @"Store\??\.Id\s*\?\?\s*0", RegexOptions.Compiled);

        private static (string file, int line, string text)[] Matching(Regex pattern)
        {
            var root = TillRoot();
            return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(f =>
                {
                    var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
                    return !rel.StartsWith("obj/") && !rel.StartsWith("bin/");
                })
                .SelectMany(f => File.ReadAllLines(f)
                    .Select((text, n) => (file: Path.GetRelativePath(root, f).Replace('\\', '/'), line: n + 1, text))
                    .Where(x => !x.text.TrimStart().StartsWith("//") && pattern.IsMatch(x.text)))
                .ToArray();
        }

        /// <summary>
        /// ⚠ ONE EXEMPTION, AND IT IS A REAL GUARD. `Database.cs` reads `.Store.Id` on the line after
        /// `if (App.GetViewModel().Store != null)`. Listing it by name is deliberate: the next
        /// dereference has to be justified out loud here rather than added quietly.
        /// </summary>
        private static readonly string[] GuardedOnTheLineAbove = { "Helpers/Database/Database.cs" };

        [Fact]
        public void Nothing_dereferences_the_legacy_store_unguarded()
        {
            var offenders = Matching(Dereference)
                .Where(x => !GuardedOnTheLineAbove.Contains(x.file))
                .Select(x => $"{x.file}:{x.line}  {x.text.Trim()}")
                .ToList();

            Assert.True(offenders.Count == 0,
                "Something dereferences `App.GetViewModel().Store` without checking it. That store is "
                + "NULL on every modern till since `EnsureStoreAsync` was deleted (step 21), so this is a "
                + "NullReferenceException waiting for whoever makes the screen reachable.\n\n"
                + "Take a local first and refuse when it is null — do NOT default the id.\n\n"
                + string.Join("\n", offenders));
        }

        /// <summary>⚠⚠ THE WRONG FIX, PINNED SO IT CANNOT BE MADE QUIETLY.</summary>
        [Fact]
        public void Nobody_invents_a_store_id_of_zero()
        {
            var offenders = Matching(InventsAnId)
                .Select(x => $"{x.file}:{x.line}  {x.text.Trim()}")
                .ToList();

            Assert.True(offenders.Count == 0,
                "A store id is being defaulted to 0. The register is explicit that this writes stock rows "
                + "against store 0 — a silent data change wearing a null-fix disguise. No store means no "
                + "stock row, which is recoverable; a row against the wrong store is not.\n\n"
                + string.Join("\n", offenders));
        }

        /// <summary>
        /// ⚠ AND THE METHOD ITSELF IS GONE. Step 21 was recorded as done once before while the method
        /// was still there — the register calls that row "wrong twice" — so the deletion is asserted
        /// rather than assumed.
        /// </summary>
        [Fact]
        public void EnsureStoreAsync_no_longer_exists()
        {
            var login = File.ReadAllText(Path.Combine(TillRoot(), "ViewModels", "LoginViewModel.cs"));

            Assert.DoesNotContain("private static async Task EnsureStoreAsync", login, StringComparison.Ordinal);
            Assert.DoesNotContain("await EnsureStoreAsync()", login, StringComparison.Ordinal);
        }
    }
}
