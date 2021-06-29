using NSwag.Annotations;
using Plutus.Entities.Models.Interface;
using System;

namespace Plutus.Repository.QueryParameters
{
    public abstract class CompositeQueryParameters<TEntity, TIdOne, TIdTwo> where TEntity : ICompositeBase<TIdOne, TIdTwo>
    {
        private const int MaxPageSize = 50;
        public int PageNumber { get; set; } = 1;

        private int _pageSize = 10;

        public int PageSize
        {
            get => _pageSize;
            set => _pageSize = (value > MaxPageSize) ? MaxPageSize : value;
        }

        public bool IgnorePagination { get; set; } = false;

        public DateTime MinCreatedDate { get; set; } = DateTime.UnixEpoch;
        public DateTime MaxCreatedDate { get; set; } = DateTime.Now;

        [OpenApiIgnore]
        public bool ValidCreatedDates => MaxCreatedDate > MinCreatedDate;

        /*public virtual Expression<Func<TEntity, bool>> GetExpression() => qP => qP.CreatedAt.Date >= MinCreatedDate.Date &&
                                                                                qP.CreatedAt.Date <= MaxCreatedDate.Date;*/
    }
}
