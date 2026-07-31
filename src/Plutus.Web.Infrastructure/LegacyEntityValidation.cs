#nullable disable

using System;
using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace Plutus.Infrastructure.Validation
{
    /// <summary>
    /// Stops the legacy CRUD controllers rejecting perfectly good writes.
    ///
    /// The problem: those controllers bind the EF ENTITY straight from the request body
    /// (<c>PUT /api/Item</c> takes an <c>Item</c>). Plutus.Entities is a nullable-enabled assembly,
    /// so MVC infers <c>[Required]</c> on every non-nullable reference property — which includes
    /// things a client must NEVER send:
    ///   • EF navigation properties (<c>Item.Cat</c>, <c>Item.Tax</c>, <c>Item.Business</c>),
    ///   • navigation COLLECTIONS (<c>Transactions</c>, <c>Refunds</c>, <c>DisItems</c>, …),
    ///   • the audit stamps the SERVER fills in on save (<c>CreatedBy</c>, <c>ModifiedBy</c>).
    /// Editing an item therefore 400'd with "The Cat field is required. The Tax field is required.
    /// The CreatedBy field is required…" — a list of fields the caller cannot supply and the server
    /// does not want.
    ///
    /// The fix, deliberately narrow:
    ///   • only properties DECLARED ON a Plutus.Entities.Models type,
    ///   • only navigation properties, collections and audit stamps,
    ///   • and only where the requirement was INFERRED — an explicit <c>[Required]</c>
    ///     (Item.Name, Item.Brand, Item.TaxId, Item.CatId) is left exactly as it is.
    /// So real validation still fires; only the impossible demands are dropped.
    ///
    /// Preferred over the global <c>SuppressImplicitRequiredAttributeForNonNullableReferenceTypes</c>
    /// switch, which would weaken validation for every DTO in the platform to fix a legacy-binding
    /// quirk. Fixes the whole class of bug at once (Item, Category, Employee, … all bind this way),
    /// not just the item edit that surfaced it.
    /// </summary>
    public sealed class LegacyEntityValidationMetadataProvider : IValidationMetadataProvider
    {
        private const string EntityNamespace = "Plutus.Entities.Models";

        /// <summary>Audit/bookkeeping columns written by RepositoryContext on save, never by a caller.</summary>
        private static readonly string[] ServerOwned = { "CreatedBy", "ModifiedBy", "CreatedAt", "ModifiedAt" };

        public void CreateValidationMetadata(ValidationMetadataProviderContext context)
        {
            if (context?.Key.ContainerType == null || context.ValidationMetadata == null) return;

            // scope: properties of a legacy entity only
            var container = context.Key.ContainerType;
            if (container.Namespace == null || !container.Namespace.StartsWith(EntityNamespace, StringComparison.Ordinal)) return;

            // an explicit [Required] is a real rule the author meant — never touch it
            if (context.PropertyAttributes?.OfType<RequiredAttribute>().Any() == true) return;

            var name = context.Key.Name ?? string.Empty;
            var type = context.Key.ModelType;

            var isServerOwned = Array.IndexOf(ServerOwned, name) >= 0;
            var isCollection = type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);
            var isNavigation = type.Namespace != null
                && type.Namespace.StartsWith(EntityNamespace, StringComparison.Ordinal)
                && type.IsClass;

            if (!isServerOwned && !isCollection && !isNavigation) return;

            // Two things to undo, because the DataAnnotations provider does two things for a
            // non-nullable reference type: it sets IsRequired AND injects a RequiredAttribute into
            // the validator list. Clearing the flag alone leaves the validator in place and the
            // request still 400s — which is exactly the trap this comment exists to stop the next
            // person falling into.
            context.ValidationMetadata.IsRequired = false;
            for (var i = context.ValidationMetadata.ValidatorMetadata.Count - 1; i >= 0; i--)
                if (context.ValidationMetadata.ValidatorMetadata[i] is RequiredAttribute)
                    context.ValidationMetadata.ValidatorMetadata.RemoveAt(i);
        }
    }
}
