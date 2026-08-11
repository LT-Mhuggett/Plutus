using Xunit;

// ─────────────────────────────────────────────────────────────────────────────
// ⚠⚠ THE INTEGRATION SUITE RUNS ONE COLLECTION AT A TIME.
//
// Found 2026-08-11 while adding `ClosedDaySaleE2eTests`: the full suite reported 2 failures on one
// run and 8 on the next, always in `CatalogueSyncE2eTests`, and that class PASSES IN ISOLATION. The
// exception was an `ObjectDisposedException` out of `WebApplicationFactory.CreateClient` →
// `EnsureServer` → `MigrateDatabase` → `EnsureCreated` — a host being built against a service
// provider another collection had already disposed.
//
// ⚠ IT IS NOT CAUSED BY THE NEW TESTS, and it is worth being exact about that: with the change under
// test stashed, the only failures were the two new tests that SHOULD fail without it. The flake is
// latent and older — 45 test classes each take `IClassFixture<PlutusAppFactory>`, xUnit runs each
// class as its own collection IN PARALLEL by default, and nothing in this project ever configured
// that. So up to 45 full ASP.NET hosts could be starting and disposing at once, and adding the
// 45th is simply what tipped this machine over.
//
// ⚠ THESE TESTS WERE NEVER SAFE TO RUN IN PARALLEL. `PlutusAppFactory` builds a real host and holds
// an in-memory SQLite connection whose lifetime is the fixture's; concurrent construction and
// disposal of those is the race. Serialising is the honest fix rather than a retry loop.
//
// ⚠ AND A FLAKY SUITE IS WORSE THAN A SLOW ONE. It teaches everybody to re-run and shrug, which is
// exactly what nearly happened here — the failures looked like collateral from a money change and
// would have been dismissed as such. 157 integration tests take about 13 seconds; determinism is
// cheap at that price.
//
// Unit and architecture suites are untouched: they have no shared host and parallelise safely.
// ─────────────────────────────────────────────────────────────────────────────
[assembly: CollectionBehavior(DisableTestParallelization = true)]
