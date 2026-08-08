using System;
using System.Collections.Concurrent;
using System.Net.Http;

namespace Plutus.Frontend.AppClient.Services.Connectivity
{
    /// <summary>
    /// One <see cref="HttpClient"/> per server address, cached.
    ///
    /// ⚠ THE BUG THIS EXISTS TO PREVENT, because it is subtle and it broke enrolment outright:
    /// <c>HttpClient.BaseAddress</c> is IMMUTABLE once the instance has sent a request — reassigning
    /// it throws *"This instance has already started one or more requests. Properties can only be
    /// modified before sending the first request."*
    ///
    /// The trap is that a "has the address changed?" guard does not save you. Assigning
    /// <c>new Uri("https://host")</c> stores it NORMALISED, as <c>https://host/</c> — so the very
    /// next comparison against the un-normalised value says "changed", reassigns, and throws. The
    /// connection check ran first, sent a request, and every later call died. Nothing about the
    /// symptom pointed at a trailing slash.
    ///
    /// So: never mutate. Build a client per address and keep it. Server addresses change about once
    /// in a device's life, so the cache holds one entry in practice — nowhere near the socket
    /// exhaustion that "new HttpClient() per call" causes, which is what the mutation was avoiding
    /// in the first place.
    /// </summary>
    internal static class PlutusHttp
    {
        /// <summary>Below the probe's own 5s budget being exceeded is the probe's business; this is
        /// the backstop that stops a hung server looking like a frozen app.</summary>
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

        private static readonly ConcurrentDictionary<string, HttpClient> Clients = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>A client bound to <paramref name="baseAddress"/>. Same address in, same instance
        /// back.</summary>
        public static HttpClient For(Uri baseAddress) =>
            Clients.GetOrAdd(baseAddress.AbsoluteUri, _ => new HttpClient
            {
                BaseAddress = baseAddress,
                Timeout = Timeout,
            });

        /// <summary>Parse a user-typed server address. Returns null when it is not usable, so the
        /// caller can say so plainly instead of throwing at them.</summary>
        public static HttpClient? TryFor(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var trimmed = url.Trim();

            // A bare host is what people type. Assume https rather than rejecting it — http would be
            // the wrong default for something carrying a device credential.
            if (!trimmed.Contains("://")) trimmed = "https://" + trimmed;

            return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
                   && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                ? For(uri)
                : null;
        }
    }
}
