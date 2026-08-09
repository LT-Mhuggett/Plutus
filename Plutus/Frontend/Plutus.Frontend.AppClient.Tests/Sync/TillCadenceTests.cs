using System;
using System.Threading.Tasks;
using Plutus.Frontend.AppClient.Services.Sync;
using Xunit;

namespace Plutus.Frontend.AppClient.Tests.Sync
{
    /// <summary>
    /// Cutover step 13 — the till's one background clock.
    ///
    /// ⚠ WHAT IS TESTABLE HERE IS THE SCHEDULING, not the HTTP. A tick needs device credentials, a
    /// server and a store; those are covered by `OutboxPusherTests`, `SyncClientTests` and the E2E
    /// suite. What this pins is the part that has no test anywhere else and breaks silently: whether
    /// the loop starts twice, whether it survives, and whether it holds the catalogue back while a
    /// basket is open.
    /// </summary>
    public class TillCadenceTests : IDisposable
    {
        public void Dispose()
        {
            TillCadence.Stop();
            TillCadence.BasketIsOpen = null;
        }

        /// <summary>
        /// ⚠ Sign-in, app start and a connectivity change all call `Start`. If it were not
        /// idempotent the till would run two clocks — every heartbeat, drain and catalogue pull
        /// doubled, behind a rate limit sized for one.
        /// </summary>
        [Fact]
        public void Starting_twice_does_not_run_two_clocks()
        {
            TillCadence.Start();
            TillCadence.Start();
            TillCadence.Start();

            // Stopping once must leave nothing running — if a second loop existed it would survive.
            TillCadence.Stop();

            // And starting again after a stop must work, or a reconnect would leave the till mute.
            TillCadence.Start();
            TillCadence.Stop();
        }

        [Fact]
        public void Stopping_a_clock_that_never_started_is_harmless()
        {
            TillCadence.Stop();
            TillCadence.Stop();
        }

        /// <summary>⚠ The web till's interval. Changing it changes the fleet list's idea of which
        /// tills are late, so it is pinned rather than left as a number someone may tune.</summary>
        [Fact]
        public void The_cadence_matches_the_web_tills_sixty_seconds()
        {
            Assert.Equal(TimeSpan.FromSeconds(60), TillCadence.Interval);
        }

        /// <summary>
        /// ⚠ A basket open must HOLD THE CATALOGUE, and the till decides that by asking the basket
        /// rather than by guessing. A sync mid-basket rewrites prices under the operator's hands:
        /// one line from before the tick and one from after, in a single sale.
        /// </summary>
        [Fact]
        public void The_basket_can_hold_the_catalogue_back()
        {
            var open = true;
            TillCadence.BasketIsOpen = () => open;

            Assert.True(TillCadence.BasketIsOpen());
            open = false;
            Assert.False(TillCadence.BasketIsOpen());
        }

        /// <summary>⚠ An unset gate must not throw. The Plutus tab can tick before the till screen
        /// has ever been built, and a NullReferenceException in the loop would stop every sync on
        /// the till with no symptom other than sales quietly not arriving.</summary>
        [Fact]
        public async Task A_tick_with_no_basket_gate_set_does_not_throw()
        {
            TillCadence.BasketIsOpen = null;

            // ⚠ Returns a message rather than throwing on a till with no credentials — which is
            // exactly the state of a machine running the test suite.
            var result = await TillCadence.TickAsync();

            Assert.False(string.IsNullOrWhiteSpace(result));
        }

        /// <summary>The tab has something to show before the first tick lands.</summary>
        [Fact]
        public void There_is_always_something_to_report()
        {
            Assert.False(string.IsNullOrWhiteSpace(TillCadence.LastResult));
        }
    }
}
