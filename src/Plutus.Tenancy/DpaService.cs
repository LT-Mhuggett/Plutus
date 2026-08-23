#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Tenancy
{
    public sealed record DpaDocumentView(
        string Version, string Title, string BodyMarkdown, DateTime? PublishedAtUtc, bool IsCurrent);

    public sealed record DpaStatusView(
        bool Accepted, string Version, DateTime? AcceptedAtUtc,
        string AcceptedByEmail, string RecordedByOperator, string CurrentVersion, bool CurrentAccepted);

    /// <summary>
    /// **WP-SIGNUP §4 — the versioned DPA, its acceptance records, and the gate.**
    ///
    /// ⚠⚠ WHAT THIS REPLACES. `PUT /api/v1/tenants/{id}/compliance` is platform-admin gated, so today
    /// the OPERATOR types a date on the client's behalf and the `dpa-missing` signal clears. Matt,
    /// 2026-08-22: *"is there somewhere that the end user can sign or agree to this? So that I am not
    /// 'Ticking it for them?'"* An operator-entered date is evidence that somebody believes a DPA was
    /// signed. It is not evidence that the client agreed to anything.
    ///
    /// ⚠ THE OPERATOR ROUTE SURVIVES, LABELLED. Some clients sign on paper. That records
    /// `RecordedByOperator` with a null `AcceptedByEmail`, and every surface must show it as
    /// "recorded manually by …" rather than as the client's own act.
    /// </summary>
    public sealed class DpaService
    {
        private readonly MySqlDbContext _db;
        public DpaService(MySqlDbContext db) => _db = db;

        /// <summary>
        /// ⚠⚠ EVERY SAVE NEEDS AN AUDIT USER AND THE ANONYMOUS SIGNUP PATH HAS NONE.
        /// `RepositoryContext.SaveMethods` throws before writing anything without one, which is how
        /// signup 500'd the first time it was called on the live server. Same fault, same shape, in
        /// `TenantApplicationService` — its StampActor carries the full account.
        /// </summary>
        private void StampActor(string actor)
        {
            if (string.IsNullOrEmpty(_db.CurrentUser)) _db.CurrentUser = actor;
        }

        /// <summary>
        /// The DPA a client is asked to accept, or null if none is published.
        ///
        /// ⚠⚠ A DRAFT IS NEVER SERVED. `PublishedAtUtc == null` means the wording is not agreed yet,
        /// and WP-signup §4.3 is explicit that recording acceptance of unfinished text is worse than
        /// having no DPA at all — it manufactures evidence that a client agreed to something nobody
        /// wrote. Signup therefore refuses to complete rather than showing a placeholder.
        /// </summary>
        public async Task<DpaDocumentView> GetCurrentAsync(CancellationToken ct = default)
        {
            var d = await _db.DpaDocuments.AsNoTracking()
                .FirstOrDefaultAsync(x => x.IsCurrent && x.PublishedAtUtc != null, ct);
            return d == null ? null : new DpaDocumentView(d.Version, d.Title, d.BodyMarkdown, d.PublishedAtUtc, d.IsCurrent);
        }

        public async Task<IReadOnlyList<DpaDocumentView>> ListAsync(CancellationToken ct = default) =>
            (await _db.DpaDocuments.AsNoTracking()
                .OrderByDescending(x => x.CreatedAtUtc)
                .ToListAsync(ct))
            .Select(d => new DpaDocumentView(d.Version, d.Title, d.BodyMarkdown, d.PublishedAtUtc, d.IsCurrent))
            .ToList();

        /// <summary>Create or replace a DRAFT. ⚠ A published version is immutable — editing the text
        /// a client has already accepted would rewrite the evidence.</summary>
        public async Task<DpaDocument> SaveDraftAsync(
            string version, string title, string bodyMarkdown, string note, string actor, CancellationToken ct = default)
        {
            StampActor(actor);
            if (string.IsNullOrWhiteSpace(version)) throw new EnrolmentException(400, "version is required.");
            if (string.IsNullOrWhiteSpace(title)) throw new EnrolmentException(400, "title is required.");
            if (string.IsNullOrWhiteSpace(bodyMarkdown)) throw new EnrolmentException(400, "The agreement body is required.");

            var existing = await _db.DpaDocuments.FirstOrDefaultAsync(x => x.Version == version, ct);
            if (existing != null && existing.PublishedAtUtc != null)
                throw new EnrolmentException(409,
                    $"Version {version} is published and cannot be edited. Publishing a changed document "
                    + "means a NEW version — an accepted version must keep the words that were accepted.");

            if (existing == null)
            {
                existing = new DpaDocument { Id = Uuid7.New(), Version = version, CreatedAtUtc = DateTime.UtcNow, CreatedBy = actor };
                _db.DpaDocuments.Add(existing);
            }

            existing.Title = title;
            existing.BodyMarkdown = bodyMarkdown;
            existing.InternalNote = note;
            await _db.SaveChangesAsync(ct);
            return existing;
        }

        /// <summary>
        /// Publish a draft and make it the current one.
        ///
        /// ⚠⚠ THIS RE-RAISES `dpa-missing` FOR EVERY TENANT THAT HAS NOT ACCEPTED THIS VERSION, and
        /// that is the point of versioning. Accepting "2026-01" is not accepting "2027-04"; a
        /// re-issued DPA silently treated as already agreed is worse than never having asked.
        /// The sweep does the raising — it compares against the CURRENT version, so flipping the flag
        /// here is what makes the signal correct.
        /// </summary>
        public async Task<DpaDocument> PublishAsync(string version, string actor, CancellationToken ct = default)
        {
            StampActor(actor);
            var doc = await _db.DpaDocuments.FirstOrDefaultAsync(x => x.Version == version, ct)
                ?? throw new EnrolmentException(404, $"No DPA version {version}.");

            if (doc.PublishedAtUtc == null)
            {
                doc.PublishedAtUtc = DateTime.UtcNow;
                doc.PublishedBy = actor;
                doc.BodySha256 = CompactToken.Sha256(doc.BodyMarkdown ?? string.Empty);
            }

            // ⚠ Exactly one current. Cleared in the same SaveChanges so there is no instant with two.
            foreach (var other in await _db.DpaDocuments.Where(x => x.IsCurrent && x.Version != version).ToListAsync(ct))
                other.IsCurrent = false;

            doc.IsCurrent = true;
            await _db.SaveChangesAsync(ct);
            return doc;
        }

        /// <summary>
        /// Record that a client accepted the current DPA. ⚠ Idempotent per (tenant, version): a
        /// double-clicked Accept must not write two evidence rows for one act.
        /// </summary>
        public async Task<DpaAcceptance> AcceptAsync(
            Guid tenantId, Guid? userId, string email, string ip, string userAgent,
            Guid? sourceApplicationId = null, CancellationToken ct = default)
        {
            StampActor("dpa-accept");
            var doc = await _db.DpaDocuments.AsNoTracking()
                .FirstOrDefaultAsync(x => x.IsCurrent && x.PublishedAtUtc != null, ct)
                ?? throw new EnrolmentException(409,
                    "There is no published DPA to accept. A draft cannot be accepted — see WP-signup §4.3.");

            var existing = await _db.DpaAcceptances
                .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.Version == doc.Version, ct);
            if (existing != null) return existing;

            var row = new DpaAcceptance
            {
                Id = Uuid7.New(),
                TenantId = tenantId,
                Version = doc.Version,
                BodySha256 = doc.BodySha256,
                AcceptedAtUtc = DateTime.UtcNow,
                AcceptedByUserId = userId,
                AcceptedByEmail = email,
                AcceptedIp = Truncate(ip, 45),
                UserAgent = Truncate(userAgent, 400),
                SourceApplicationId = sourceApplicationId,
            };
            _db.DpaAcceptances.Add(row);

            // The legacy fields stay in step so the existing signal and the Subscribers screen agree.
            await StampTenantAsync(tenantId, doc.Version, row.AcceptedAtUtc, ct);
            await _db.SaveChangesAsync(ct);
            return row;
        }

        /// <summary>
        /// ⚠⚠ THE PAPER ROUTE, AND IT MUST NEVER MASQUERADE AS THE CLIENT'S OWN ACT. Sets
        /// `RecordedByOperator` and leaves `AcceptedByEmail` null, which is what every surface reads
        /// to render "recorded manually by …" instead of "accepted by".
        /// </summary>
        public async Task<DpaAcceptance> RecordManuallyAsync(
            Guid tenantId, string version, string operatorName, string note, CancellationToken ct = default)
        {
            StampActor(string.IsNullOrWhiteSpace(operatorName) ? "platform-admin" : operatorName);
            if (string.IsNullOrWhiteSpace(operatorName))
                throw new EnrolmentException(400, "An operator name is required — an unattributed manual record is not evidence.");

            var doc = await _db.DpaDocuments.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Version == version && x.PublishedAtUtc != null, ct)
                ?? throw new EnrolmentException(404, $"No published DPA version {version}.");

            var existing = await _db.DpaAcceptances
                .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.Version == version, ct);
            if (existing != null) return existing;

            var row = new DpaAcceptance
            {
                Id = Uuid7.New(),
                TenantId = tenantId,
                Version = version,
                BodySha256 = doc.BodySha256,
                AcceptedAtUtc = DateTime.UtcNow,
                RecordedByOperator = operatorName,
                OperatorNote = note,
            };
            _db.DpaAcceptances.Add(row);
            await StampTenantAsync(tenantId, version, row.AcceptedAtUtc, ct);
            await _db.SaveChangesAsync(ct);
            return row;
        }

        /// <summary>Where a tenant stands against the CURRENT version — what the banner and the
        /// compliance signal both read.</summary>
        public async Task<DpaStatusView> StatusAsync(Guid tenantId, CancellationToken ct = default)
        {
            var current = await _db.DpaDocuments.AsNoTracking()
                .FirstOrDefaultAsync(x => x.IsCurrent && x.PublishedAtUtc != null, ct);

            var accepted = await _db.DpaAcceptances.AsNoTracking()
                .Where(a => a.TenantId == tenantId)
                .OrderByDescending(a => a.AcceptedAtUtc)
                .FirstOrDefaultAsync(ct);

            var currentAccepted = current != null && accepted != null
                && await _db.DpaAcceptances.AsNoTracking()
                    .AnyAsync(a => a.TenantId == tenantId && a.Version == current.Version, ct);

            return new DpaStatusView(
                Accepted: accepted != null,
                Version: accepted?.Version,
                AcceptedAtUtc: accepted?.AcceptedAtUtc,
                AcceptedByEmail: accepted?.AcceptedByEmail,
                RecordedByOperator: accepted?.RecordedByOperator,
                CurrentVersion: current?.Version,
                CurrentAccepted: currentAccepted);
        }

        /// <summary>
        /// ⚠ `IgnoreQueryFilters` because this runs on an UNSCOPED platform context (signup and the
        /// operator console both do) — without it the tenant row is invisible and the stamp silently
        /// does nothing, leaving the acceptance recorded and the signal still red.
        /// </summary>
        private async Task StampTenantAsync(Guid tenantId, string version, DateTime whenUtc, CancellationToken ct)
        {
            var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
            if (tenant == null) return;
            tenant.DpaSignedAtUtc = whenUtc;
            tenant.DpaRef = version;
        }

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max));
    }
}
