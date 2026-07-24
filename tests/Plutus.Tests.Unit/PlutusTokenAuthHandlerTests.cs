using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Plutus.Identity;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>WP1.2: the test-env auth handler turns a signed Bearer token into the claims that
/// real scope policies evaluate — operator vs device, with expiry + signature checks.</summary>
public class PlutusTokenAuthHandlerTests
{
    private const string Secret = "handler-test-secret";

    private sealed class Mon : IOptionsMonitor<AuthenticationSchemeOptions>
    {
        public AuthenticationSchemeOptions CurrentValue { get; } = new();
        public AuthenticationSchemeOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<AuthenticationSchemeOptions, string?> listener) => null;
    }

    private static async Task<AuthenticateResult> Authenticate(string? bearer)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TEST_TOKEN_SECRET"] = Secret })
            .Build();
        var handler = new PlutusTokenAuthHandler(new Mon(), NullLoggerFactory.Instance, UrlEncoder.Default, config);
        var http = new DefaultHttpContext();
        if (bearer != null) http.Request.Headers.Authorization = $"Bearer {bearer}";
        await handler.InitializeAsync(
            new AuthenticationScheme(PlutusTokenAuthHandler.SchemeName, null, typeof(PlutusTokenAuthHandler)), http);
        return await handler.AuthenticateAsync();
    }

    private static string Token(object payload) => CompactToken.Issue(JsonSerializer.Serialize(payload), Secret);
    private static long Soon => DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
    private static long Past => DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds();

    [Fact]
    public async Task Operator_token_emits_scope_and_identity_claims()
    {
        var emp = Guid.NewGuid();
        var result = await Authenticate(Token(new { EmployeeId = emp, Name = "Ada", Scope = "platform-admin pos.sell", Exp = Soon }));

        Assert.True(result.Succeeded);
        var scopes = result.Principal!.FindAll("scope").Select(c => c.Value).ToArray();
        Assert.Contains("platform-admin", scopes);
        Assert.Contains("pos.sell", scopes);
        Assert.Equal(emp.ToString(), result.Principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
    }

    [Fact]
    public async Task Device_token_emits_tid_did_and_device_scope()
    {
        var tid = Guid.NewGuid();
        var did = Guid.NewGuid();
        var result = await Authenticate(Token(new { tid = tid.ToString(), did = did.ToString(), scope = "device", exp = Soon }));

        Assert.True(result.Succeeded);
        Assert.Equal(tid.ToString(), result.Principal!.FindFirst("tid")!.Value);
        Assert.Equal(did.ToString(), result.Principal.FindFirst("did")!.Value);
        Assert.Equal("device", result.Principal.FindFirst("scope")!.Value);
    }

    [Fact]
    public async Task Expired_token_fails()
    {
        var result = await Authenticate(Token(new { EmployeeId = Guid.NewGuid(), Exp = Past }));
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Tampered_signature_fails()
    {
        var good = Token(new { EmployeeId = Guid.NewGuid(), Exp = Soon });
        var tampered = good[..^2] + (good[^1] == 'A' ? "BB" : "AA");
        var result = await Authenticate(tampered);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task No_bearer_is_noresult()
    {
        var result = await Authenticate(null);
        Assert.False(result.Succeeded);
        Assert.False(result.Failure != null); // NoResult, not a hard failure
    }
}
