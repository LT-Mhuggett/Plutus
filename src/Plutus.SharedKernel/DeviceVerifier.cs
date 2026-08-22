using System;
using System.Security.Cryptography;
using System.Text;

namespace Plutus.SharedKernel;

/// <summary>
/// **A password verifier that only works on ONE till — step 28, 2026-08-22.**
///
/// ⚠⚠ **THE PROBLEM THIS SOLVES IS IN `OfflineCredentials`' OWN HEADER:** *"A stolen till holds
/// hashes at PBKDF2-SHA1/101,010 — roughly 13× below current OWASP guidance for that PRF. **Those
/// are the operators' PLATFORM passwords, and they work on the web till too.**"*
///
/// Today the roster ships every operator's platform credential to every till, and `OperatorLogin`
/// verifies the typed password against it. So a till taken out of a shop is a bag of platform
/// passwords for the whole staff — crackable offline at leisure, and useful on the portal, the web
/// till and anything else those people can sign into.
///
/// ⚠⚠ **A DEVICE VERIFIER IS NOT A PASSWORD HASH — IT IS A LOCAL ARTEFACT.** It is minted on this
/// till, from this till's own random salt, the first time somebody signs in **online**. Cracking it
/// yields the password, so it is not magic; what it buys is:
///
///   · **A LEAVER CANNOT BE PRE-LOADED.** A till that has never seen an account online holds nothing
///     for it, so a stolen till exposes only the people who actually used it.
///   · **THE ITERATION COUNT IS OURS.** The platform hash is pinned at SHA-1/101,010 by a legacy
///     format nothing may change without a migration. This one is new, so it is SHA-256 at a modern
///     count — see <see cref="Iterations"/>.
///   · **IT IS REVOCABLE LOCALLY.** Deleting it re-imposes "connect once", which is the only lever a
///     till has ever had over a credential it already holds.
///
/// ⚠ **IT DOES NOT REPLACE THE HORIZONS** (`OfflineCredentials`). Those answer *"how long may this
/// till trust what it knows"*; this answers *"what does it know, and is it worth stealing"*. A
/// verifier past the sell horizon is still refused.
///
/// ⚠ **AND IT IS NOT THE WHOLE OF STEP 28.** The server keeps shipping platform hashes until every
/// till mints verifiers; stopping that is a separate, flagged change with its own C2 row. Until
/// then this reduces what a NEW theft yields, not what an old one did.
/// </summary>
public static class DeviceVerifier
{
    /// <summary>
    /// ⚠⚠ SHA-256, NOT SHA-1, AND 600,000 ROUNDS. OWASP's 2023 guidance for PBKDF2-HMAC-SHA256 is
    /// 600,000; the platform hash this replaces is SHA-1 at 101,010, which that same guidance puts
    /// ~13× too low. This format is NEW, so nothing forces the old parameters on it — and choosing
    /// them anyway "for consistency" would have been the whole point of the exercise, thrown away.
    ///
    /// ⚠ IT IS RECORDED IN THE STORED RECORD, not only here. A verifier minted today must still
    /// verify after this constant is raised, or every till in the estate demands a reconnection on
    /// the deploy that hardens it.
    /// </summary>
    public const int Iterations = 600_000;

    /// <summary>⚠ 32 bytes, fresh per mint. The salt is what makes this till's verifier useless on
    /// another one, and reusing a salt across devices would undo that in one line.</summary>
    public const int SaltBytes = 32;

    /// <summary>32 bytes out of SHA-256 — the PRF's natural output. Asking for more just re-runs the
    /// derivation and buys nothing.</summary>
    public const int HashBytes = 32;

    /// <summary>The algorithm label stored with a verifier, so a future one can be told apart.</summary>
    public const string Algorithm = "PBKDF2-SHA256";

    /// <summary>
    /// One operator's verifier, as this till stores it.
    /// </summary>
    /// <param name="UserId">Who it is for.</param>
    /// <param name="SaltBase64">This device's random salt for this operator.</param>
    /// <param name="HashBase64">The derived verifier.</param>
    /// <param name="Iterations">⚠ STORED, NOT ASSUMED — see <see cref="DeviceVerifier.Iterations"/>.
    /// A verifier minted before the count was raised must still verify, or hardening the constant
    /// locks every operator out of every till until each reconnects.</param>
    /// <param name="Algorithm">⚠ Likewise: a record whose algorithm this build does not know is
    /// treated as absent, which re-imposes "connect once" rather than failing a sign-in oddly.</param>
    /// <param name="MintedAtUtc">When this till first saw the account online. ⚠ Diagnostic, and the
    /// answer to "when did this machine last talk to the platform about this person".</param>
    public sealed record Record(
        Guid UserId,
        string SaltBase64,
        string HashBase64,
        int Iterations,
        string Algorithm,
        DateTime MintedAtUtc);

    /// <summary>
    /// Mint a verifier for this device from a password that has JUST been proved online.
    /// </summary>
    /// <remarks>
    /// ⚠⚠ ONLY EVER CALL THIS AFTER THE SERVER HAS SAID YES. Minting from an unverified password
    /// would let anyone standing at an offline till enrol their own password for somebody else's
    /// account — the exact door step 28 exists to shut, installed backwards.
    /// </remarks>
    public static Record Mint(Guid userId, string password, DateTime nowUtc)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(password ?? string.Empty, salt, Iterations);

        return new Record(
            userId,
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash),
            Iterations,
            Algorithm,
            nowUtc);
    }

    /// <summary>
    /// Does this password match the verifier this till holds?
    /// </summary>
    /// <remarks>
    /// ⚠ FALSE FOR ANYTHING IT CANNOT READ — a null record, a corrupt base64, an algorithm this
    /// build does not know. ⚠⚠ AND THE CALLER MUST TREAT "cannot read" AS "no verifier", not as a
    /// wrong password: the first sends somebody to reconnect, the second tells them they have
    /// forgotten a password they typed correctly. See <see cref="CanVerify"/>.
    ///
    /// ⚠ Fixed-time comparison, as everywhere else — a verifier is only worth having if it does not
    /// leak its own answer by how long it takes to say no.
    /// </remarks>
    public static bool Verify(Record? record, string password)
    {
        if (!CanVerify(record)) return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(record!.SaltBase64);
            expected = Convert.FromBase64String(record.HashBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        var computed = Derive(password ?? string.Empty, salt, record.Iterations);
        return CryptographicOperations.FixedTimeEquals(computed, expected);
    }

    /// <summary>
    /// Is this a verifier this build can actually check against?
    ///
    /// ⚠⚠ THE DISTINCTION MATTERS MORE THAN IT LOOKS. "No usable verifier" must route to *connect
    /// once*, and "usable verifier, wrong password" to *wrong password*. Collapsing them gives an
    /// operator who mistyped a lecture about the network, and an operator on a fresh till a lecture
    /// about their password.
    ///
    /// ⚠ An iteration count of zero or below is refused rather than run: a corrupt record must not
    /// be able to turn a verification into an instant one.
    /// </summary>
    public static bool CanVerify(Record? record) =>
        record is not null
        && !string.IsNullOrWhiteSpace(record.SaltBase64)
        && !string.IsNullOrWhiteSpace(record.HashBase64)
        && record.Iterations > 0
        && string.Equals(record.Algorithm, Algorithm, StringComparison.Ordinal);

    private static byte[] Derive(string secret, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(secret), salt, iterations, HashAlgorithmName.SHA256, HashBytes);
}
