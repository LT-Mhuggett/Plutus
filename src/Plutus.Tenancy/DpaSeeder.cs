#nullable disable

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Tenancy
{
    /// <summary>
    /// Puts Matt's DPA into the database **as a draft**, once, so it is reviewable and editable in
    /// Platform → DPA without a redeploy.
    ///
    /// ⚠⚠⚠ IT IS SEEDED UNPUBLISHED AND MUST NOT BE PUBLISHED AS IT STANDS. Two things are wrong
    /// with the supplied document for THIS purpose, and both were checked against the file
    /// (`Build/To do/Data_Processing_Agreement_Leading_Talent.docx`, read 2026-08-23):
    ///
    /// **1. THE PARTIES ARE THE WRONG WAY ROUND.** The document names *"LEADING TALENT [LIMITED]
    /// (the "Controller")"* and `[PROCESSOR NAME]` as the Processor. That is the shape for Leading
    /// Talent ENGAGING a supplier — a payroll bureau, say. In Plutus signup the relationship is the
    /// other way: **the shop is the Controller** of its customers' and staff's personal data, and
    /// **Plutus/Leading Talent is the Processor** acting on the shop's instructions. Publishing it
    /// unchanged would ask every shop to agree that Leading Talent controls their data and that they
    /// process it — the opposite of the truth, and worth nothing if it were ever relied on.
    ///
    /// **2. IT IS AN UNFILLED TEMPLATE.** Its own first page says *"TEMPLATE — for review by a
    /// qualified solicitor before use"*, and 18 distinct `[…]` placeholders remain: company number,
    /// registered address, the date, the breach-notification window (`[24/48]` hours), the audit
    /// notice period, the retention period, the whole of Schedule 3's sub-processor list, and an
    /// unmade either/or on whether sub-processor authorisation is specific or general.
    ///
    /// ⚠ WP-signup §4.3 anticipated exactly this: *"shipping a signup that records acceptance of
    /// placeholder text would be worse than having no DPA at all — it manufactures evidence that a
    /// client agreed to something nobody wrote."* So the mechanism is complete and the document is
    /// inert: `GetCurrentAsync` will not serve a draft, `AcceptAsync` refuses, and signup returns 409
    /// rather than showing placeholder wording.
    ///
    /// **To go live:** correct the parties, fill the brackets, have a solicitor read it, save it as a
    /// new version in Platform → DPA, and publish that.
    /// </summary>
    public static class DpaSeeder
    {
        /// <summary>⚠ "DRAFT-" prefixed on purpose: the version string appears in every acceptance
        /// record, so a draft that somehow got published would be self-identifying in the evidence.</summary>
        public const string DraftVersion = "DRAFT-2026-08";

        public static async Task<bool> SeedAsync(MySqlDbContext db, CancellationToken ct = default)
        {
            // ⚠ Only ever seeds into an EMPTY table. Once anybody has drafted or published anything,
            // this must never touch it again — re-seeding over an edited draft would silently
            // discard a solicitor's changes.
            if (await db.DpaDocuments.AnyAsync(ct)) return false;

            var body = ReadEmbedded("dpa-draft.md");
            if (string.IsNullOrWhiteSpace(body)) return false;

            db.CurrentUser ??= "dpa-seeder";
            db.DpaDocuments.Add(new DpaDocument
            {
                Id = Uuid7.New(),
                Version = DraftVersion,
                Title = "Data Processing Agreement (DRAFT — not for use)",
                BodyMarkdown = body,
                PublishedAtUtc = null,        // ⚠⚠ DRAFT. Cannot be served or accepted.
                IsCurrent = false,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedBy = "dpa-seeder",
                InternalNote =
                    "⚠⚠ DO NOT PUBLISH AS-IS. Two blockers: (1) the parties are reversed — this names "
                    + "Leading Talent as the CONTROLLER and the other side as Processor, which is the "
                    + "shape for engaging a supplier. For Plutus the SHOP is the Controller and "
                    + "Plutus/Leading Talent is the Processor. (2) It is an unfilled template — its own "
                    + "first page says \"for review by a qualified solicitor before use\", and 18 "
                    + "placeholders remain (company number, address, breach window, audit notice, "
                    + "retention period, Schedule 3 sub-processors, and the specific-vs-general "
                    + "sub-processor authorisation choice). Correct the parties, fill the brackets, "
                    + "have it reviewed, then save it as a NEW version and publish that.",
            });

            await db.SaveChangesAsync(ct);
            return true;
        }

        private static string ReadEmbedded(string fileName)
        {
            var asm = Assembly.GetExecutingAssembly();
            var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(fileName, StringComparison.Ordinal));
            if (name == null) return null;
            using var stream = asm.GetManifestResourceStream(name);
            if (stream == null) return null;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
