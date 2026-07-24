#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Identity
{
    /// <summary>A scope node on the reporting spine Tenant → Company → Store → Till.</summary>
    public readonly record struct ScopeNode(RbacScopeType Type, string Id)
    {
        public static readonly ScopeNode Tenant = new(RbacScopeType.Tenant, "");
        public static ScopeNode Company(Guid businessId) => new(RbacScopeType.Company, businessId.ToString("D").ToLowerInvariant());
        public static ScopeNode Store(int storeId) => new(RbacScopeType.Store, storeId.ToString());
        public static ScopeNode Till(Guid tillId) => new(RbacScopeType.Till, tillId.ToString("D").ToLowerInvariant());

        /// <summary>Parses "tenant" | "company:{guid}" | "store:{int}" | "till:{guid}"
        /// (the `scope=` query format of the effective-permissions endpoint).</summary>
        public static bool TryParse(string s, out ScopeNode node)
        {
            node = Tenant;
            if (string.IsNullOrWhiteSpace(s) || s.Equals("tenant", StringComparison.OrdinalIgnoreCase)) return true;
            var parts = s.Split(':', 2);
            if (parts.Length != 2) return false;
            switch (parts[0].ToLowerInvariant())
            {
                case "company" when Guid.TryParse(parts[1], out var c): node = Company(c); return true;
                case "store" when int.TryParse(parts[1], out var st): node = Store(st); return true;
                case "till" when Guid.TryParse(parts[1], out var t): node = Till(t); return true;
                default: return false;
            }
        }

        public override string ToString() => Type == RbacScopeType.Tenant ? "tenant" : $"{Type.ToString().ToLowerInvariant()}:{Id}";
    }

    /// <summary>
    /// WP3.1 resolution (architecture §7.2): effective permissions at a resource = the UNION of
    /// grants from every assignment at that node or ABOVE it on the spine (till → its store →
    /// its company → tenant). Time-windowed assignments only count while inside their window —
    /// evaluated at token issue (and re-evaluated by the till locally when offline). Per code,
    /// an unlimited grant beats any ceiling; otherwise the highest ceiling wins.
    /// </summary>
    public sealed class EffectivePermissionsService
    {
        private readonly MySqlDbContext _db;
        public EffectivePermissionsService(MySqlDbContext db) => _db = db;

        /// <summary>The scope chain from the node up to tenant, resolved against the legacy
        /// hierarchy (Till.StoreId, Store.BusinessId). Unknown nodes still yield tenant scope
        /// (an assignment at tenant level covers everything, even a mis-keyed node).</summary>
        public async Task<IReadOnlyList<ScopeNode>> AncestorChainAsync(ScopeNode node)
        {
            var chain = new List<ScopeNode> { node };
            switch (node.Type)
            {
                case RbacScopeType.Till when Guid.TryParse(node.Id, out var tillId):
                {
                    var till = await _db.Till.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tillId);
                    if (till != null)
                    {
                        chain.Add(ScopeNode.Store(till.StoreId));
                        var store = await _db.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.Id == till.StoreId);
                        if (store != null) chain.Add(ScopeNode.Company(store.BusinessId));
                    }
                    break;
                }
                case RbacScopeType.Store when int.TryParse(node.Id, out var storeId):
                {
                    var store = await _db.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.Id == storeId);
                    if (store != null) chain.Add(ScopeNode.Company(store.BusinessId));
                    break;
                }
            }
            if (node.Type != RbacScopeType.Tenant) chain.Add(ScopeNode.Tenant);
            return chain;
        }

        /// <summary>Effective permissions for a user at a scope node, at local time
        /// <paramref name="nowLocal"/> (time windows run on the store's wall clock).</summary>
        public async Task<IReadOnlyList<EffectivePermission>> ResolveAsync(
            Guid userId, ScopeNode scope, DateTime nowLocal)
        {
            var chain = await AncestorChainAsync(scope);
            var types = chain.Select(c => (byte)c.Type).ToArray();

            var assignments = await _db.RbacRoleAssignments.AsNoTracking()
                .Include(a => a.Role).ThenInclude(r => r.Grants)
                .Where(a => a.UserId == userId)
                .ToListAsync();

            var applicable = assignments
                .Where(a => chain.Contains(new ScopeNode(a.ScopeType, a.ScopeId)))
                .Where(a => InWindow(a, nowLocal));

            return EffectivePermission.Merge(
                applicable.SelectMany(a => a.Role.Grants)
                    .Select(g => new EffectivePermission(g.PermissionCode, g.MaxPence)));
        }

        /// <summary>True when the user has ANY role assignment — used by login to decide
        /// between RBAC-derived scopes and the pre-seed fallback.</summary>
        public Task<bool> HasAnyAssignmentsAsync(Guid userId) =>
            _db.RbacRoleAssignments.AsNoTracking().AnyAsync(a => a.UserId == userId);

        /// <summary>Does this user hold <paramref name="permissionCode"/> anywhere in the
        /// tenant? Used for coarse portal gates when no finer resource node is known.</summary>
        public async Task<bool> HasAnywhereAsync(Guid userId, string permissionCode, DateTime nowLocal)
        {
            var assignments = await _db.RbacRoleAssignments.AsNoTracking()
                .Include(a => a.Role).ThenInclude(r => r.Grants)
                .Where(a => a.UserId == userId)
                .ToListAsync();
            return assignments.Where(a => InWindow(a, nowLocal))
                .SelectMany(a => a.Role.Grants)
                .Any(g => g.PermissionCode == permissionCode);
        }

        public static bool InWindow(RbacRoleAssignment a, DateTime nowLocal)
        {
            var nowUtc = nowLocal.Kind == DateTimeKind.Utc ? nowLocal : nowLocal.ToUniversalTime();
            if (a.ValidFromUtc.HasValue && nowUtc < a.ValidFromUtc.Value) return false;
            if (a.ValidToUtc.HasValue && nowUtc > a.ValidToUtc.Value) return false;

            if (a.DaysOfWeekMask.HasValue &&
                (a.DaysOfWeekMask.Value & (1 << (int)nowLocal.DayOfWeek)) == 0) return false;

            if (a.WindowStartLocal.HasValue || a.WindowEndLocal.HasValue)
            {
                var t = TimeOnly.FromDateTime(nowLocal);
                if (a.WindowStartLocal.HasValue && t < a.WindowStartLocal.Value) return false;
                if (a.WindowEndLocal.HasValue && t > a.WindowEndLocal.Value) return false;
            }
            return true;
        }
    }
}
