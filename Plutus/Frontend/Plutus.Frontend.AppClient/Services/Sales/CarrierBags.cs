using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Contracts.Client;

namespace Plutus.Frontend.AppClient.Services.Sales
{
    /// <summary>
    /// The carrier bags this shop sells, as the portal set them — ruling 2026-08-19.
    ///
    /// ⚠⚠ MATT: *"That creates the 5p and 20p bags at the back and that pushes down to the tills… This
    /// would be cleaner than creating a bag at each till."* This till used to hold ONE bag barcode in
    /// `Preferences` (`DefaultBagId`), set per device, so a five-till shop configured it five times and
    /// the tills could disagree about what a bag costs.
    ///
    /// ⚠⚠ **THE FALLBACK DIRECTION IS THE OPPOSITE OF <see cref="Reporting.PublishedReports"/>, ON
    /// PURPOSE.** That class fails towards MORE reports, because a till that loses its Reports tab looks
    /// broken. This one fails towards NO BAG BUTTONS:
    ///
    ///   1. what the server said just now;
    ///   2. the last-known-good cached in <c>Preferences</c> — survives a restart and an offline day;
    ///   3. **nothing**, when this till has never once had an answer.
    ///
    /// The third step is the whole point. Inventing a bag price charges a customer money the shop never
    /// set, which is a refund and a complaint; a missing button is a cashier keying an item.
    ///
    /// ⚠⚠ C2 TWIN of `SharedKernel.CarrierBags` (the id/price rule) and the web till's
    /// `till/carrierBags.ts`. All three decide what makes an id a bag and what to do with a row whose
    /// price contradicts it, and two tills that disagree offer different bags for the same shop.
    /// Vectors: `CarrierBagTests` in C#, `carrierBags.test.ts` in TypeScript.
    /// </summary>
    internal static class CarrierBags
    {
        /// <summary>⚠ Versioned so a future change of shape cannot be read as a valid old value.</summary>
        private const string CacheKey = "plutus.carrierbags.v1";

        private static readonly SemaphoreSlim Gate = new(1, 1);

        /// <summary>
        /// Raised when the list actually CHANGED, so a page redraws its buttons and not otherwise.
        ///
        /// ⚠ Arrives on the cadence's BACKGROUND thread — a subscriber writing an
        /// <c>ObservableCollection</c> must marshal itself, exactly as the noticeboard's does.
        ///
        /// ⚠ STATIC, so a page that subscribes must unsubscribe: a `Disappearing` that forgets holds
        /// the page alive for the life of the process and stacks a handler per visit.
        /// </summary>
        internal static event Action Changed;

        /// <summary>
        /// The bags to offer, cheapest first.
        ///
        /// ⚠ Reads the CACHE only — never the network. It is called from a viewmodel constructor and on
        /// the UI thread, and a till must never block on a server to draw a button.
        /// </summary>
        internal static IReadOnlyList<CarrierBagDto> Bags
        {
            get
            {
                IReadOnlyList<CarrierBagDto> cached = Load();

                // ⚠ NULL = never had an answer → NO BAGS. Not "a default bag": see the class remarks.
                return cached ?? Array.Empty<CarrierBagDto>();
            }
        }

        /// <summary>
        /// Ask the server and update the cache. ⚠ Returns true when the answer CHANGED, so a screen
        /// rebuilds its buttons only when it must.
        ///
        /// ⚠ Never throws. It runs on the settings cadence; an exception here would be an `async void`
        /// escape, which is how this app has been killed before.
        /// </summary>
        internal static async Task<bool> RefreshAsync(CancellationToken ct = default)
        {
            await Gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var api = await Connectivity.PlutusApi.GetAsync(ct).ConfigureAwait(false);
                if (api is null) return false;   // ⚠ Not connected is not a change.

                var answer = await api.GetCarrierBagsAsync(ct).ConfigureAwait(false);

                // ⚠⚠ NULL IS "COULD NOT ASK", NOT "NO BAGS". Caching an empty list here would drop the
                // buttons on the first failed request and keep them gone offline.
                if (answer is null) return false;

                var incoming = Sellable(answer);
                var current = Load();

                var changed = current is null || !Same(current, incoming);
                if (changed)
                {
                    Save(incoming);

                    // ⚠ Raised INSIDE the gate but after the save, so a subscriber that reads `Bags`
                    // straight away sees the new list rather than the one it just replaced.
                    // ⚠ A subscriber that throws must not take the tick down with it.
                    try { Changed?.Invoke(); }
                    catch (Exception ex) { Analytics.CrashLog.Write("CarrierBags.Changed", ex); }
                }

                return changed;
            }
            catch (Exception ex)
            {
                Analytics.CrashLog.Write("CarrierBags.Refresh", ex);
                return false;
            }
            finally
            {
                Gate.Release();
            }
        }

        /// <summary>
        /// Keep only the rows that can honestly be sold, cheapest first.
        ///
        /// ⚠⚠ A ROW THAT DOES NOT ADD UP IS DROPPED, NOT REPAIRED. A bag whose price contradicts its own
        /// id means the item was edited behind the portal's back, and there is no way to know which
        /// figure is right — so it does not become a button. Refusing to sell is recoverable; selling at
        /// a made-up price is not.
        ///
        /// ⚠ Sorted here rather than trusting the server, so the button order cannot be reshuffled by
        /// however the list happened to be serialised. The single-use bag is asked for many times a day
        /// and the bag for life occasionally, so the common one sits under the operator's thumb.
        /// </summary>
        private static List<CarrierBagDto> Sellable(IEnumerable<CarrierBagDto> rows)
        {
            var clean = new List<CarrierBagDto>();

            foreach (var row in rows ?? Enumerable.Empty<CarrierBagDto>())
            {
                if (row is null) continue;
                if (SharedKernel.CarrierBags.PriceFromId(row.IdOne) is not long encoded) continue;
                if (encoded != row.PricePence) continue;

                // ⚠ A blank name would draw a nameless button; the price is the one thing always true.
                var name = string.IsNullOrWhiteSpace(row.Name)
                    ? SharedKernel.CarrierBags.DefaultNameFor(row.PricePence)
                    : row.Name;

                clean.Add(new CarrierBagDto(row.IdOne, name, row.PricePence));
            }

            return clean.OrderBy(b => b.PricePence).ToList();
        }

        private static bool Same(IReadOnlyList<CarrierBagDto> a, IReadOnlyList<CarrierBagDto> b) =>
            a.Count == b.Count
            && a.Zip(b).All(p => string.Equals(p.First.IdOne, p.Second.IdOne, StringComparison.Ordinal)
                                 && string.Equals(p.First.Name, p.Second.Name, StringComparison.Ordinal)
                                 && p.First.PricePence == p.Second.PricePence);

        /// <summary>
        /// The cached list, or <see langword="null"/> when this till has never had an answer.
        ///
        /// ⚠ A stored EMPTY list is returned as empty, not as null — "the shop sells no bags" is a real
        /// state and must survive a restart. Both end up showing no buttons; the difference matters to
        /// <see cref="RefreshAsync"/>, which must not treat a real "none" as a reason to keep asking.
        /// </summary>
        private static List<CarrierBagDto> Load()
        {
            try
            {
                var json = Microsoft.Maui.Storage.Preferences.Get(CacheKey, null);
                if (string.IsNullOrWhiteSpace(json)) return null;

                // ⚠ Re-filtered on the way out, not trusted: a cache written by an older build could
                // hold a row this one would refuse to sell.
                return Sellable(JsonSerializer.Deserialize<List<CarrierBagDto>>(json));
            }
            catch (Exception ex)
            {
                // ⚠ A corrupt cache reads as "never asked", which shows no bag buttons. That is the safe
                // direction here — see the class remarks.
                Analytics.CrashLog.Write("CarrierBags.Load", ex);
                return null;
            }
        }

        private static void Save(IReadOnlyList<CarrierBagDto> bags)
        {
            try
            {
                Microsoft.Maui.Storage.Preferences.Set(CacheKey, JsonSerializer.Serialize(bags));
            }
            catch (Exception ex)
            {
                Analytics.CrashLog.Write("CarrierBags.Save", ex);
            }
        }
    }
}
