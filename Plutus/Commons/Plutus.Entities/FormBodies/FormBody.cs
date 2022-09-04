using Plutus.Entities.Models;
using System;

namespace Plutus.Entities.FormBodies
{
    public class FormBody<TEntity> where TEntity : Auditable, new()
    {
        public DateTime? CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? ModifiedAt { get; set; }
        public string? ModifiedBy { get; set; }

        public FormBody()
        {

        }

        public FormBody(TEntity entity)
        {
            CreatedAt = entity.CreatedAt;
            CreatedBy = entity.CreatedBy;
            ModifiedAt = entity.ModifiedAt;
            ModifiedBy = entity.ModifiedBy;
        }

        public virtual TEntity GenerateEntity()
        {

            if (CreatedAt != default && ModifiedAt != default)
            {
#pragma warning disable CS8629 // Nullable value type may be null.
#pragma warning disable CS8601 // Possible null reference assignment.
                return new()
                {
                    CreatedAt = (DateTime)CreatedAt,
                    CreatedBy = CreatedBy,
                    ModifiedAt = (DateTime)ModifiedAt,
                    ModifiedBy = ModifiedBy,
                };
#pragma warning restore CS8601 // Possible null reference assignment.
#pragma warning restore CS8629 // Nullable value type may be null.
            }
            else
            {
                return new();
            }
        }
    }
}
