using System;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// Step 28 — the device-local verifier itself.
///
/// ⚠ `OnlineFirstSignInTests` owns the DECISION (when a verifier is minted and consulted); this owns
/// the artefact.
/// </summary>
public class DeviceVerifierTests
{
    private static readonly Guid Who = Guid.Parse("01931f3c-0000-7000-8000-0000000000bb");
    private static readonly DateTime Now = new(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_minted_verifier_accepts_the_password_it_was_minted_from()
    {
        var r = DeviceVerifier.Mint(Who, "correct horse", Now);

        Assert.True(DeviceVerifier.Verify(r, "correct horse"));
        Assert.False(DeviceVerifier.Verify(r, "correct horse "));   // ⚠ not trimmed — a password is bytes
        Assert.False(DeviceVerifier.Verify(r, "Correct Horse"));
        Assert.False(DeviceVerifier.Verify(r, ""));
    }

    /// <summary>
    /// ⚠⚠ THE SALT IS WHAT MAKES IT A **DEVICE** VERIFIER. Two mints of the same password must
    /// produce different records, or a verifier lifted off one till would verify on another and the
    /// whole point of minting locally is gone.
    /// </summary>
    [Fact]
    public void Two_mints_of_the_same_password_differ()
    {
        var a = DeviceVerifier.Mint(Who, "correct horse", Now);
        var b = DeviceVerifier.Mint(Who, "correct horse", Now);

        Assert.NotEqual(a.SaltBase64, b.SaltBase64);
        Assert.NotEqual(a.HashBase64, b.HashBase64);

        // ⚠ And each still verifies its own — different is not broken.
        Assert.True(DeviceVerifier.Verify(a, "correct horse"));
        Assert.True(DeviceVerifier.Verify(b, "correct horse"));
    }

    /// <summary>
    /// ⚠⚠ STRONGER THAN THE THING IT REPLACES, AND THAT IS THE POINT. `OfflineCredentials`' header
    /// says the platform hash sits at PBKDF2-SHA1/101,010, *"roughly 13× below current OWASP
    /// guidance"*. This format is new, so nothing forced those parameters on it.
    /// </summary>
    [Fact]
    public void It_is_sha256_at_the_owasp_count_not_the_legacy_sha1_one()
    {
        Assert.Equal("PBKDF2-SHA256", DeviceVerifier.Algorithm);
        Assert.Equal(600_000, DeviceVerifier.Iterations);
        Assert.True(DeviceVerifier.Iterations > Pbkdf2.Iterations * 5,
            "the device verifier must not inherit the legacy iteration count");
    }

    /// <summary>
    /// ⚠⚠ THE ITERATION COUNT IS READ FROM THE RECORD, NOT THE CONSTANT. A verifier minted before
    /// the constant is raised must still verify — otherwise the deploy that hardens it locks every
    /// operator out of every till until each one reconnects.
    /// </summary>
    [Fact]
    public void A_verifier_minted_at_an_older_count_still_verifies()
    {
        var current = DeviceVerifier.Mint(Who, "correct horse", Now);

        // Same salt, same password, fewer rounds — what a record minted before a hardening looks
        // like. Recomputed here the way the old build would have.
        var salt = Convert.FromBase64String(current.SaltBase64);
        var oldHash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            System.Text.Encoding.UTF8.GetBytes("correct horse"), salt, 210_000,
            System.Security.Cryptography.HashAlgorithmName.SHA256, DeviceVerifier.HashBytes);

        var older = current with { HashBase64 = Convert.ToBase64String(oldHash), Iterations = 210_000 };

        Assert.True(DeviceVerifier.Verify(older, "correct horse"));
        Assert.False(DeviceVerifier.Verify(older, "wrong horse"));
    }

    /// <summary>
    /// ⚠⚠ "CANNOT READ" IS NOT "WRONG PASSWORD", and the caller depends on the distinction: one
    /// sends somebody to reconnect, the other tells them they forgot a password they typed right.
    /// </summary>
    [Fact]
    public void An_unusable_record_reports_itself_as_unusable()
    {
        var good = DeviceVerifier.Mint(Who, "correct horse", Now);

        Assert.False(DeviceVerifier.CanVerify(null));
        Assert.False(DeviceVerifier.CanVerify(good with { SaltBase64 = "" }));
        Assert.False(DeviceVerifier.CanVerify(good with { HashBase64 = "   " }));
        Assert.False(DeviceVerifier.CanVerify(good with { Algorithm = "PBKDF2-SHA1" }));
        Assert.True(DeviceVerifier.CanVerify(good));
    }

    /// <summary>⚠ A zero or negative iteration count is refused rather than run — a corrupt record
    /// must not be able to turn a verification into an instant one.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_nonsense_iteration_count_is_refused(int iterations)
    {
        var r = DeviceVerifier.Mint(Who, "correct horse", Now) with { Iterations = iterations };

        Assert.False(DeviceVerifier.CanVerify(r));
        Assert.False(DeviceVerifier.Verify(r, "correct horse"));
    }

    /// <summary>⚠ Corrupt base64 is false, never an exception — a damaged store must not crash a
    /// login screen.</summary>
    [Fact]
    public void Corrupt_base64_is_false_not_a_throw()
    {
        var r = DeviceVerifier.Mint(Who, "correct horse", Now) with { SaltBase64 = "not-base64!!" };

        Assert.False(DeviceVerifier.Verify(r, "correct horse"));
    }

    [Fact]
    public void It_records_who_and_when()
    {
        var r = DeviceVerifier.Mint(Who, "correct horse", Now);

        Assert.Equal(Who, r.UserId);
        Assert.Equal(Now, r.MintedAtUtc);
    }
}
