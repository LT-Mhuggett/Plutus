using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Plutus.Contracts.Client;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.SharedKernel;

namespace Plutus.Frontend.AppClient.Services.Inventory
{
    /// <summary>
    /// Category management on the till (WP10 / cutover step 25) — create, rename, reassign, delete.
    ///
    /// ⚠ THE 409 IS THE FEATURE, NOT AN ERROR. `DELETE /api/v1/categories/{id}` refuses while any
    /// item still references the category, and says how many. That refusal exists to guard a legacy
    /// cascade: `Item → Category` is `OnDelete(Cascade)`, so deleting a category on the LEGACY route
    /// takes every item in it — and their sale lines and stock with them. A client that treats the
    /// 409 as a failure and stops has removed the only safe route through, which is why this flow
    /// turns it into the OFFER to reassign rather than into a message.
    ///
    /// ⚠ MAUI had no reassign UI at all, so the server's 409 had nowhere to land. That is the Part B
    /// gap this closes.
    ///
    /// ⚠ Built from action sheets on purpose. Every other multi-step choice on this till is one —
    /// tenders, item search, refund origins, tax bands — and a bespoke page here would be a second
    /// idiom for the same job on the same screen.
    /// </summary>
    internal static class CategoryManager
    {
        /// <summary>Open the manager. Returns true if anything changed, so the caller can re-sync.</summary>
        public static async Task<bool> ShowAsync()
        {
            var gate = Security.TillGate.Check(
                App.GetViewModel().SignedInOperator, PermissionCatalogue.PortalStockAdjust);

            if (!gate.Allowed)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), gate.Message, "OK".Translate());
                return false;
            }

            var api = await Connectivity.PlutusApi.GetOperatorAsync();
            if (api is null)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "Managing categories needs someone signed in and a connection to Plutus.", "OK".Translate());
                return false;
            }

            var changed = false;

            // ⚠ A LOOP, because these operations chain: you delete a category, are told 12 items are
            // in it, move them, and delete again. Bouncing the operator back to the item list
            // between each step would make the one flow that NEEDS continuity the only one without it.
            while (true)
            {
                var categories = await api.GetCategoryListAsync();
                if (categories is null)
                {
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "Plutus wouldn't send back the categories. Nothing has been changed.", "OK".Translate());
                    return changed;
                }

                const string addNew = "＋ New category…";

                // ⚠ The COUNT is on every row, because it is what decides whether a delete can
                // happen at all — and it counts BINNED items too, so a category that looks empty
                // can still refuse. Showing it up front makes the refusal predictable.
                var labels = categories
                    .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(c => $"{c.Name}  ({c.ItemCount} item{(c.ItemCount == 1 ? "" : "s")})")
                    .ToList();

                var ordered = categories.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
                labels.Add(addNew);

                var picked = await UIHandeling.Modal.ShowAsync(() =>
                    Plutus.Frontend.AppClient.Helpers.CustomViews.ChoiceHelper.AskAsync(
                        "Categories", "Done", null, labels.ToArray()));

                if (string.IsNullOrWhiteSpace(picked) || picked == "Done") return changed;

                if (picked == addNew)
                {
                    if (await CreateAsync(api)) changed = true;
                    continue;
                }

                var index = labels.IndexOf(picked);
                if (index < 0 || index >= ordered.Count) continue;

                if (await ActOnAsync(api, ordered[index], ordered)) changed = true;
            }
        }

        private static async Task<bool> CreateAsync(Plutus.Client.Core.PlutusApiClient api)
        {
            var name = await Application.Current.MainPage.DisplayPromptAsync(
                "New category", "What is it called?", "OK".Translate(), "Cancel".Translate(), maxLength: 60);

            if (string.IsNullOrWhiteSpace(name)) return false;

            var (ok, problem) = await api.CreateCategoryAsync(name.Trim());
            if (!ok)
            {
                // ⚠ The server's words — a 409 here means the NAME is taken, which is the one thing
                // the operator can act on.
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    problem ?? "Plutus wouldn't create that category.", "OK".Translate());
                return false;
            }

            await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                $"Added the “{name.Trim()}” category.", "OK".Translate());
            return true;
        }

        private static async Task<bool> ActOnAsync(
            Plutus.Client.Core.PlutusApiClient api, CategoryListDto category, IReadOnlyList<CategoryListDto> all)
        {
            // ⚠ EVERY LABEL SAYS "CATEGORY", and that is a direct response to the hand-run. Matt,
            // 2026-08-11: *"There is also a 'Delete item'. This needs to not delete an item, this
            // should be controlled on the portal."* There is no delete-item anywhere in the till —
            // the only "Delete…" is THIS one, and it deletes a CATEGORY. On a screen reached from a
            // list of items, a bare "Delete…" reads as deleting the item you were just looking at.
            //
            // ⚠ He is right about the underlying worry even though the button was not what he
            // thought: an item must never be deletable from a till, because deleting one cascades
            // to its sale lines and its stock. The till's withdrawal action is the BIN, which is
            // reversible and keeps the item's history. That has not changed — this is a wording fix
            // so nobody has to find out by pressing it.
            const string rename = "Rename this category…";
            const string move = "Move its items to another category…";
            const string delete = "Delete this category…";

            var picked = await UIHandeling.Modal.ShowAsync(() =>
                Plutus.Frontend.AppClient.Helpers.CustomViews.ChoiceHelper.AskAsync(
                    $"Category: {category.Name} — {category.ItemCount} item{(category.ItemCount == 1 ? "" : "s")}",
                    "Cancel".Translate(), null, rename, move, delete));

            if (picked == rename) return await RenameAsync(api, category);
            if (picked == move) return await ReassignAsync(api, category, all);
            if (picked == delete) return await DeleteAsync(api, category, all);
            return false;
        }

        private static async Task<bool> RenameAsync(
            Plutus.Client.Core.PlutusApiClient api, CategoryListDto category)
        {
            var name = await Application.Current.MainPage.DisplayPromptAsync(
                "Rename category", $"A new name for “{category.Name}”.",
                "OK".Translate(), "Cancel".Translate(), initialValue: category.Name, maxLength: 60);

            if (string.IsNullOrWhiteSpace(name) || name.Trim() == category.Name) return false;

            var (ok, problem) = await api.RenameCategoryAsync(category.Id, name.Trim());
            if (!ok)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    problem ?? "Plutus wouldn't rename that category.", "OK".Translate());
                return false;
            }

            return true;
        }

        /// <summary>
        /// Move every item out of this category into another.
        ///
        /// ⚠ IT IS ALL OR NOTHING AND THE COUNT IS IN THE CONFIRMATION. The endpoint has no partial
        /// reassign and no cap — it moves the whole category — so the operator is told how many
        /// items are about to move BEFORE it happens. "Move its items" sounds selective; it is not.
        /// </summary>
        private static async Task<bool> ReassignAsync(
            Plutus.Client.Core.PlutusApiClient api, CategoryListDto from, IReadOnlyList<CategoryListDto> all)
        {
            if (from.ItemCount == 0)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    $"“{from.Name}” has no items to move.", "OK".Translate());
                return false;
            }

            var targets = all.Where(c => c.Id != from.Id).ToList();
            if (targets.Count == 0)
            {
                // ⚠ `Item.CatId` is REQUIRED, so there is nowhere for the items to go. Saying that
                // is more useful than an empty picker.
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "There is nowhere to move them — this is the only category. Create another first.",
                    "OK".Translate());
                return false;
            }

            var labels = targets.Select(c => c.Name ?? c.Id.ToString("D")).ToArray();

            var picked = await UIHandeling.Modal.ShowAsync(() =>
                Plutus.Frontend.AppClient.Helpers.CustomViews.ChoiceHelper.AskAsync(
                    $"Move all {from.ItemCount} items to…", "Cancel".Translate(), null, labels));

            if (string.IsNullOrWhiteSpace(picked) || picked == "Cancel".Translate()) return false;

            var index = Array.IndexOf(labels, picked);
            if (index < 0) return false;

            var to = targets[index];

            var confirmed = await UIHandeling.Modal.ShowAsync(() =>
                Application.Current.MainPage.DisplayAlert(
                    "Move them?",
                    $"All {from.ItemCount} items in “{from.Name}” will move to “{to.Name}”. " +
                    "There is no undo — moving them back means doing this again in reverse.",
                    "Move", "Cancel".Translate()));

            if (!confirmed) return false;

            var (ok, problem) = await api.ReassignCategoryAsync(from.Id, to.Id);
            if (!ok)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    problem ?? "Plutus wouldn't move those items.", "OK".Translate());
                return false;
            }

            await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                $"Moved {from.ItemCount} item{(from.ItemCount == 1 ? "" : "s")} to “{to.Name}”.", "OK".Translate());
            return true;
        }

        /// <summary>
        /// Delete a category — and turn the server's refusal into the way through.
        ///
        /// ⚠ THIS IS THE REASSIGN-FIRST FLOW. The 409 is not an error to report; it is the server
        /// saying "items are still in here", and the useful next step is to offer to move them.
        /// Reporting it and stopping is what left MAUI with no route to a delete at all.
        /// </summary>
        private static async Task<bool> DeleteAsync(
            Plutus.Client.Core.PlutusApiClient api, CategoryListDto category, IReadOnlyList<CategoryListDto> all)
        {
            // ⚠ Asked BEFORE the call when we can already see why it would fail. A confirmation
            // followed by a refusal is two dialogs to learn one thing.
            if (category.ItemCount > 0)
            {
                var moveNow = await UIHandeling.Modal.ShowAsync(() =>
                    Application.Current.MainPage.DisplayAlert(
                        $"“{category.Name}” still has items",
                        $"{category.ItemCount} item{(category.ItemCount == 1 ? " is" : "s are")} still in this " +
                        "category, and every item must have one. Move them somewhere else first?",
                        "Move them…", "Cancel".Translate()));

                if (!moveNow) return false;
                return await ReassignAsync(api, category, all);
            }

            var confirmed = await UIHandeling.Modal.ShowAsync(() =>
                Application.Current.MainPage.DisplayAlert(
                    "Delete this category?",
                    $"The CATEGORY “{category.Name}” is empty and will be removed. " +
                    "⚠ No items are deleted — items cannot be deleted from a till at all. " +
                    "To withdraw an item from sale, use Move to the Bin.",
                    "Delete the category", "Cancel".Translate()));

            if (!confirmed) return false;

            var (ok, problem) = await api.DeleteCategoryAsync(category.Id);
            if (!ok)
            {
                // ⚠ STILL POSSIBLE, and worth handling honestly: `ItemCount` counts BINNED items
                // too, and the list could be a moment out of date. The server's message names the
                // real count.
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    problem ?? "Plutus wouldn't delete that category.", "OK".Translate());
                return false;
            }

            return true;
        }
    }
}
