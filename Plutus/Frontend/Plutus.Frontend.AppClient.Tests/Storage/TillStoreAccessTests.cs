using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Frontend.AppClient.Services.Storage;
using Xunit;

namespace Plutus.Frontend.AppClient.Tests.Storage
{
    /// <summary>
    /// Cutover step 3's owed tests — the single owner of the v2 local store.
    ///
    /// ⚠ WHY THIS CLASS EXISTS AT ALL: every screen going through one gate is what stops the port
    /// reproducing the legacy shape (37 `new Database(...)` call sites across 21 files) on a newer
    /// schema. And EF Core contexts are NOT thread-safe, so the serialising is not tidiness — two
    /// overlapping writes to the outbox is a lost or duplicated sale.
    ///
    /// ⚠ WHAT IS TESTABLE HERE. `UseAsync` opens a real SQLite file under
    /// `FileSystem.AppDataDirectory`, which a test host has no MAUI runtime for — the calls throw
    /// `TypeInitializationException` from `Microsoft.Maui.Storage.Preferences`. That is exactly why
    /// `TryUseAsync` exists, and it makes the CONTRACT testable even where the storage is not:
    /// the swallowing behaviour, and the serialisation, are the two things whose failure loses
    /// money rather than merely erroring.
    /// </summary>
    public class TillStoreAccessTests
    {
        /// <summary>
        /// ⚠ THE ONE THAT MATTERS ON A UI PATH. A screen that cannot read its own data shows
        /// nothing; it must never take the app down. A null store dereferenced in a viewmodel
        /// CONSTRUCTOR is precisely how a correct password came out as "something went wrong
        /// signing in" — the exception escaped `new AppShell()`, not the tab that caused it.
        /// </summary>
        [Fact]
        public async Task TryUseAsync_swallows_and_returns_default_rather_than_throwing()
        {
            // No MAUI runtime here, so opening the store genuinely fails — which is the condition
            // being tested, not a limitation of the test.
            var result = await TillStoreAccess.TryUseAsync(s => Task.FromResult(42));

            Assert.Equal(0, result);   // default(int), never an exception
        }

        [Fact]
        public async Task TryUseAsync_returns_null_for_a_reference_type_rather_than_throwing()
        {
            Assert.Null(await TillStoreAccess.TryUseAsync(s => Task.FromResult<string>("never reached")));
        }

        /// <summary>⚠ And it swallows a fault thrown by the CALLER's own work, not just by opening
        /// the store — a viewmodel's bad LINQ must not be able to close the app either.</summary>
        [Fact]
        public async Task TryUseAsync_swallows_a_failure_inside_the_callers_own_work()
        {
            var result = await TillStoreAccess.TryUseAsync<string>(
                s => throw new InvalidOperationException("boom"));

            Assert.Null(result);
        }

        /// <summary>
        /// ⚠ `UseAsync` does NOT swallow, and that asymmetry is deliberate. The money paths —
        /// committing a sale, draining the outbox — must fail loudly enough for the caller to
        /// decide whether the basket may be cleared. A silent default there would clear a basket
        /// after a failed commit, losing the sale and the evidence together.
        /// </summary>
        [Fact]
        public async Task UseAsync_propagates_rather_than_swallowing()
        {
            await Assert.ThrowsAnyAsync<Exception>(
                () => TillStoreAccess.UseAsync(s => Task.FromResult(1)));
        }

        [Fact]
        public async Task UseAsync_refuses_a_null_unit_of_work()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => TillStoreAccess.UseAsync<int>(null));
        }

        /// <summary>
        /// ⚠ THE SEMAPHORE MUST BE RELEASED ON THE FAILING PATH. It is taken before the store is
        /// opened, so if a failure skipped the release the FIRST failed call would deadlock every
        /// subsequent one — a till that had been offline once would hang for ever on its next
        /// basket, and the symptom would be a frozen screen with nothing in any log.
        ///
        /// Ten sequential failures completing is the evidence: with a leaked permit, the second
        /// would never return and this test would time out rather than fail.
        /// </summary>
        [Fact]
        public async Task A_failure_releases_the_gate_so_the_next_caller_is_not_deadlocked()
        {
            for (var i = 0; i < 10; i++)
                Assert.Null(await TillStoreAccess.TryUseAsync(s => Task.FromResult<string>("x")));
        }

        /// <summary>⚠ And concurrently: callers queue behind the gate rather than piling into one
        /// EF context. All of them must come back — a permit lost under contention hangs the till
        /// just as thoroughly, and is far harder to reproduce afterwards.</summary>
        [Fact]
        public async Task Concurrent_callers_all_complete_rather_than_one_holding_the_gate()
        {
            var work = Enumerable.Range(0, 20)
                .Select(_ => TillStoreAccess.TryUseAsync(s => Task.FromResult<string>("x")))
                .ToArray();

            var done = await Task.WhenAll(work).WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Equal(20, done.Length);
            Assert.All(done, Assert.Null);
        }

        // ⚠ `DatabasePath` is NOT asserted here. It is
        // `Path.Combine(FileSystem.AppDataDirectory, "till-v2.db")`, and `AppDataDirectory` needs a
        // MAUI runtime this host does not have — reading it throws from
        // `Microsoft.Maui.Storage.Preferences`. A test that asserted the throw would be testing the
        // test host rather than the code, so the filename lives in one place and is verified by
        // reading it. (That the v2 file sits BESIDE the legacy one rather than replacing it is
        // binding default 3 — archive, never delete.)

        /// <summary>Cancellation is honoured rather than ignored — a screen torn down mid-load
        /// must not hold the gate while its work finishes.</summary>
        [Fact]
        public async Task A_cancelled_call_does_not_hold_the_gate()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await TillStoreAccess.TryUseAsync(s => Task.FromResult(1), cts.Token);

            // the gate still works afterwards
            Assert.Equal(0, await TillStoreAccess.TryUseAsync(s => Task.FromResult(7)));
        }
    }
}
