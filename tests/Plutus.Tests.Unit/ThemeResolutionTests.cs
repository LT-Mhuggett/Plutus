using System;
using System.Collections.Generic;
using Plutus.Entities.Models;
using Plutus.Tenancy;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>FE10: the theme-precedence rules, pinned before any UI existed. Till > Group >
/// Store > Tenant > default, and a till in several themed groups takes the newest assignment.</summary>
public class ThemeResolutionTests
{
    private static readonly Guid Till = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid GroupA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid GroupB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static TillThemeAssignment A(byte scope, string key, string theme, int minutesAgo = 0) => new()
    {
        Id = Guid.NewGuid(), TenantId = Guid.Empty, Scope = scope, ScopeKey = key, ThemeKey = theme,
        UpdatedAtUtc = new DateTime(2026, 8, 7, 12, 0, 0, DateTimeKind.Utc).AddMinutes(-minutesAgo),
    };

    [Fact]
    public void Most_specific_scope_wins_till_over_group_over_store_over_tenant()
    {
        var all = new List<TillThemeAssignment>
        {
            A(ThemeResolution.ScopeTenant, "", "builtin:dark"),
            A(ThemeResolution.ScopeStore, "7", "theme-store"),
            A(ThemeResolution.ScopeGroup, ThemeResolution.KeyFor(GroupA), "theme-group"),
            A(ThemeResolution.ScopeTill, ThemeResolution.KeyFor(Till), "builtin:light"),
        };

        var (picked, source) = ThemeResolution.Pick(all, Till, 7, new[] { GroupA });
        Assert.Equal("till", source);
        Assert.Equal("builtin:light", picked!.ThemeKey);

        // remove the till assignment → group
        all.RemoveAt(3);
        (picked, source) = ThemeResolution.Pick(all, Till, 7, new[] { GroupA });
        Assert.Equal("group", source);
        Assert.Equal("theme-group", picked!.ThemeKey);

        // remove the group assignment → store
        all.RemoveAt(2);
        (picked, source) = ThemeResolution.Pick(all, Till, 7, new[] { GroupA });
        Assert.Equal("store", source);

        // remove the store assignment → tenant
        all.RemoveAt(1);
        (picked, source) = ThemeResolution.Pick(all, Till, 7, new[] { GroupA });
        Assert.Equal("tenant", source);
        Assert.Equal("builtin:dark", picked!.ThemeKey);
    }

    [Fact]
    public void Till_in_two_themed_groups_takes_the_newest_assignment()
    {
        var all = new List<TillThemeAssignment>
        {
            A(ThemeResolution.ScopeGroup, ThemeResolution.KeyFor(GroupA), "older", minutesAgo: 60),
            A(ThemeResolution.ScopeGroup, ThemeResolution.KeyFor(GroupB), "newer", minutesAgo: 5),
        };
        var (picked, source) = ThemeResolution.Pick(all, Till, null, new[] { GroupA, GroupB });
        Assert.Equal("group", source);
        Assert.Equal("newer", picked!.ThemeKey);
    }

    [Fact]
    public void No_assignments_is_the_built_in_default_and_unrelated_scopes_do_not_leak()
    {
        var (picked, source) = ThemeResolution.Pick(Array.Empty<TillThemeAssignment>(), Till, 7, Array.Empty<Guid>());
        Assert.Null(picked);
        Assert.Equal("default", source);

        // another till's / another store's assignments never apply
        var others = new List<TillThemeAssignment>
        {
            A(ThemeResolution.ScopeTill, ThemeResolution.KeyFor(GroupA), "not-mine"), // some other till
            A(ThemeResolution.ScopeStore, "99", "not-my-store"),
        };
        (picked, source) = ThemeResolution.Pick(others, Till, 7, Array.Empty<Guid>());
        Assert.Null(picked);
        Assert.Equal("default", source);
    }

    [Fact]
    public void Guid_scope_keys_match_case_insensitively()
    {
        var upper = new TillThemeAssignment
        {
            Id = Guid.NewGuid(), TenantId = Guid.Empty, Scope = ThemeResolution.ScopeTill,
            ScopeKey = Till.ToString("D").ToUpperInvariant(), ThemeKey = "builtin:light",
            UpdatedAtUtc = DateTime.UtcNow,
        };
        var (picked, source) = ThemeResolution.Pick(new[] { upper }, Till, null, Array.Empty<Guid>());
        Assert.Equal("till", source);
        Assert.NotNull(picked);
    }
}
