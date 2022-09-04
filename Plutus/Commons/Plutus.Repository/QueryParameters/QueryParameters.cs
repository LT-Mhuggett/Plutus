using Newtonsoft.Json;
using NSwag.Annotations;
using Plutus.Entities.Models.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Web;

namespace Plutus.Repository.QueryParameters
{
    public abstract class QueryParameters<TEntity, TId> where TEntity : IBase<TId>
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
        public DateTime MinUpdatedDate { get; set; } = DateTime.UnixEpoch;
        public DateTime MaxUpdatedDate { get; set; } = DateTime.Now;

        [JsonIgnore]
        [OpenApiIgnore]
        public bool ValidCreatedDates => MaxCreatedDate > MinCreatedDate;

        public virtual Expression<Func<TEntity, bool>> GetExpression() => qP => qP.CreatedAt.Date >= MinCreatedDate.Date &&
                                                                                qP.CreatedAt.Date <= MaxCreatedDate.Date &&
                                                                                qP.ModifiedAt.Date >= MinUpdatedDate.Date &&
                                                                                qP.ModifiedAt.Date <= MaxCreatedDate.Date;

        public virtual string GetStringRepresentation()
        {
            var step1 = JsonConvert.SerializeObject(this);
            var step2 = JsonConvert.DeserializeObject<IDictionary<string, string>>(step1);
            var step3 = step2.Select(x => HttpUtility.UrlEncode(x.Key) + "=" + HttpUtility.UrlEncode(x.Value));
            return String.Join("&", step3);
        }
    }
}
