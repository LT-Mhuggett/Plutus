using System;
using System.Collections.Generic;
using System.Linq;

namespace Plutus.Migration.Kapow
{
    /// <summary>What a delta run would do — computed before anything is written, printable as-is.</summary>
    public sealed class KapowDeltaPlan
    {
        public IReadOnlyList<KapowSaleInput> ToImport { get; init; } = Array.Empty<KapowSaleInput>();
        public int InBackup { get; init; }
        public int AlreadyRecorded { get; init; }
        public int AlreadyQuarantined { get; init; }
        public long FirstDeviceSeq { get; init; }
        public string? MinDate { get; init; }
        public string? MaxDate { get; init; }

        public override string ToString() =>
            $"delta plan:\n" +
            $"  in backup            : {InBackup}\n" +
            $"  already recorded     : {AlreadyRecorded}  (skipped by LegacyRef)\n" +
            $"  already quarantined  : {AlreadyQuarantined}  (skipped — same data would quarantine again)\n" +
            $"  to import            : {ToImport.Count}" +
            (ToImport.Count > 0 ? $"  ({MinDate} → {MaxDate}), DeviceSeq from {FirstDeviceSeq}" : "");
    }

    /// <summary>
    /// Turns "a full NatApp backup" into "only the sales the target does not already have" —
    /// NatApp-Translation plan §3.2. The 2026-07 ETL was one-shot (run twice = every sale twice,
    /// blocked only by the (TenantId, DeviceId, DeviceSeq) unique index); this makes a NEWER
    /// backup of the same till safely importable, which is the live need: the physical till kept
    /// trading after the 23_07 seed, so its later backups are the only record of those sales.
    /// </summary>
    public static class KapowDelta
    {
        /// <summary>
        /// Filter to the sales the target does not have, and re-stamp identity so the result can
        /// coexist with what it does.
        ///
        /// ⚠⚠ SEQ IS RE-ASSIGNED HERE AND MUST BE. The reader numbers sales 1..N over the whole
        /// FILE (synthetic order), and the original run burned 1..21,653 for this device — the
        /// unique index (TenantId, DeviceId, DeviceSeq) means a second run numbering from 1 dies
        /// on the first insert (best case) or interleaves into historical gaps (worst — the
        /// quarantined sales' numbers, silently claiming a dead sale's slot). New sales number
        /// from MAX(existing)+1, in DateOfSale order so sequence still roughly means time.
        ///
        /// ⚠ ALREADY-QUARANTINED refs are skipped, not retried: the overlap between backups is
        /// byte-identical (verified before any run — the till only appends), so remapping them
        /// reproduces the same failure and, before the dedupe, logged it again every run. A sale
        /// whose data a FUTURE backup actually fixes would need its quarantine row resolved first,
        /// which is a human decision, not a re-run side effect.
        ///
        /// ⚠ Ordering is by <see cref="KapowSaleInput.DateOfSaleLocal"/> (ISO-ish, string-sortable),
        /// NOT by LegacyId — the legacy id is an UNPADDED timestamp string ("202681…" sorts before
        /// "2026724…"), so id order scrambles August before July.
        /// </summary>
        public static KapowDeltaPlan Plan(
            IReadOnlyList<KapowSaleInput> inputs,
            IReadOnlySet<string> recordedLegacyRefs,
            IReadOnlySet<string> quarantinedLegacyRefs,
            long maxExistingDeviceSeq)
        {
            var alreadyRecorded = 0;
            var alreadyQuarantined = 0;
            var fresh = new List<KapowSaleInput>();

            foreach (var input in inputs)
            {
                // ⚠ Recorded wins over quarantined when a ref somehow appears in both — the sale
                // EXISTS, so it must not be imported again, whatever the quarantine log says.
                if (recordedLegacyRefs.Contains(input.LegacyId)) alreadyRecorded++;
                else if (quarantinedLegacyRefs.Contains(input.LegacyId)) alreadyQuarantined++;
                else fresh.Add(input);
            }

            var ordered = fresh
                .OrderBy(s => s.DateOfSaleLocal ?? s.CreatedLocal, StringComparer.Ordinal)
                .ToList();

            var seq = maxExistingDeviceSeq;
            foreach (var s in ordered) s.DeviceSeq = ++seq;

            return new KapowDeltaPlan
            {
                ToImport = ordered,
                InBackup = inputs.Count,
                AlreadyRecorded = alreadyRecorded,
                AlreadyQuarantined = alreadyQuarantined,
                FirstDeviceSeq = ordered.Count > 0 ? maxExistingDeviceSeq + 1 : 0,
                MinDate = ordered.FirstOrDefault()?.DateOfSaleLocal,
                MaxDate = ordered.LastOrDefault()?.DateOfSaleLocal,
            };
        }
    }
}
