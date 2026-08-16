using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Plutus.Frontend.AppClient.ViewModels.MainTill.Till;
using Xunit;

namespace Plutus.Frontend.AppClient.Tests.Notices
{
    /// <summary>
    /// ⚠⚠ MAUI BINDINGS FAIL SILENTLY. A binding to a property that is not there renders **blank** and
    /// never throws — no exception, no log line, nothing on screen. That is repo-runbook pitfall
    /// territory and it has cost this project real sessions: a renumbered grid produced blank buttons
    /// that nobody could explain.
    ///
    /// The noticeboard is the worst possible place for it, because the failure looks exactly like
    /// success: an empty banner is what a till with no notices is SUPPOSED to show. A typo here means
    /// pick-from-floor notes stop reaching the shop floor and every screen looks perfectly normal.
    ///
    /// So this reads the actual XAML and checks every name it binds really exists.
    /// </summary>
    public class NoticeboardBindingTests
    {
        /// <summary>
        /// ⚠ Walks up from the test binary to the repo root rather than hardcoding a path — the test
        /// runs from `bin/Debug/...` and the depth changes with the TFM.
        /// </summary>
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);

            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Plutus.slnx")))
                dir = dir.Parent;

            Assert.NotNull(dir);   // ⚠ If this ever fails the test is not running from the repo
            return dir.FullName;
        }

        private static string TillViewXaml() => File.ReadAllText(Path.Combine(
            RepoRoot(), "Plutus", "Frontend", "Plutus.Frontend.AppClient",
            "Views", "MainTill", "Till", "TillView.xaml"));

        /// <summary>Just the noticeboard block, so this test does not police the rest of a 900-line page.</summary>
        private static string BannerMarkup()
        {
            var xaml = TillViewXaml();

            var start = xaml.IndexOf("THE NOTICEBOARD (WP5b)", StringComparison.Ordinal);
            Assert.True(start > 0, "The noticeboard block's marker comment has moved or gone.");

            var end = xaml.IndexOf("Orientation=\"Horizontal\" HorizontalOptions=\"FillAndExpand\"",
                start, StringComparison.Ordinal);
            Assert.True(end > start, "Could not find the end of the noticeboard block.");

            return xaml[start..end];
        }

        private static bool HasMember(Type t, string name) =>
            t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance) is not null;

        /// <summary>
        /// ⚠⚠ Every `{Binding Notices.X}` on the page must be a real member of
        /// <see cref="NoticeboardViewModel"/>. These are the bindings that decide whether the banner
        /// appears at all — `HasAnything` mistyped means the banner is invisible forever, silently.
        /// </summary>
        [Fact]
        public void Every_Notices_binding_on_the_page_exists_on_the_viewmodel()
        {
            var names = Regex.Matches(TillViewXaml(), @"Binding\s+(?:Path=)?Notices\.(\w+)")
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .ToList();

            // ⚠ If this is empty the regex has rotted, and a green test would mean nothing.
            Assert.NotEmpty(names);

            var missing = names.Where(n => !HasMember(typeof(NoticeboardViewModel), n)).ToList();

            Assert.True(missing.Count == 0,
                $"TillView.xaml binds Notices.{{{string.Join(", ", missing)}}}, which "
                + "NoticeboardViewModel does not have. MAUI renders that blank and never throws.");
        }

        /// <summary>
        /// ⚠⚠ And every per-row `{Binding X}` inside the item template must exist on
        /// <see cref="NoticeRow"/> — the row is a flattened shape precisely so this can be checked.
        /// </summary>
        [Fact]
        public void Every_row_binding_in_the_banner_exists_on_the_row()
        {
            var names = Regex.Matches(BannerMarkup(), @"\{Binding\s+(\w+)\}")
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .Where(n => n != "BindingContext")
                .ToList();

            Assert.NotEmpty(names);

            var missing = names
                .Where(n => !HasMember(typeof(NoticeRow), n) && !HasMember(typeof(NoticeboardViewModel), n))
                .ToList();

            Assert.True(missing.Count == 0,
                $"The noticeboard template binds {{{string.Join(", ", missing)}}}, which is on "
                + "neither NoticeRow nor NoticeboardViewModel.");
        }

        /// <summary>
        /// ⚠ The Done button reaches the page's viewmodel through `x:Reference`, so the page MUST
        /// carry that name. Without it the button silently does nothing when pressed — which on this
        /// banner means an operator acknowledging a pick note that never gets acknowledged.
        /// </summary>
        [Fact]
        public void The_ack_button_can_reach_the_pages_command()
        {
            var xaml = TillViewXaml();

            Assert.Contains("x:Name=\"TillPage\"", xaml);
            Assert.Contains("Source={x:Reference Name=TillPage}", BannerMarkup());
            Assert.True(HasMember(typeof(NoticeboardViewModel), "AckCommand"));
        }

        /// <summary>
        /// ⚠ `.Translate()` and `{i18n:Translate}` THROW in DEBUG on a key that does not exist
        /// (repo-runbook pitfall 18) — on the SELLING screen, which would take the till down rather
        /// than merely look wrong. Every key the banner names must be in the resx.
        /// </summary>
        [Fact]
        public void Every_translated_key_in_the_banner_is_a_real_resource()
        {
            var keys = Regex.Matches(BannerMarkup(), @"\{i18n:Translate\s+(\w+)\}")
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .ToList();

            Assert.NotEmpty(keys);

            var resx = File.ReadAllText(Path.Combine(
                RepoRoot(), "Plutus", "Shared", "I18N_L10N", "Resx", "AppResources.resx"));

            var missing = keys.Where(k => !resx.Contains($"name=\"{k}\"", StringComparison.Ordinal)).ToList();

            Assert.True(missing.Count == 0,
                $"The noticeboard translates {{{string.Join(", ", missing)}}}, which AppResources.resx "
                + "does not define. That throws in DEBUG, on the selling screen.");
        }
    }
}
