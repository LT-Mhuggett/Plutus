using System;
using System.Collections.Generic;
using System.Linq;

namespace Plutus.Repository.Extensions
{
    public class PagedList<T> : List<T>
    {
        public int CurrentPage { get; private set; }
        public int TotalPages { get; private set; }
        public int PageSize { get; private set; }
        public int TotalCount { get; private set; }
        public bool HasPrevious => CurrentPage > 1;
        public bool HasNext => CurrentPage < TotalPages;

        public object MetaData => new
        {
            TotalCount,
            PageSize,
            CurrentPage,
            TotalPages,
            HasNext,
            HasPrevious
        };

        public PagedList()
        {

        }

        public PagedList(List<T> items, int count, int pageNumber, int pageSize)
        {
            TotalCount = count;
            PageSize = pageSize;
            CurrentPage = pageNumber;
            TotalPages = (int)Math.Ceiling(count / (double)pageSize);

            AddRange(items);
        }

        public static PagedList<T> ToPagedList(IQueryable<T> source, int pageNumber, int pageSize, bool ignorePaging)
        {
            var count = source.Count();
            if (ignorePaging)
            {
                pageSize = count;
                pageNumber = 1;
            }
            if (pageSize == 0)
                return new PagedList<T>();

            var items = source.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();

            return new PagedList<T>(items, count, pageNumber, pageSize);
        }

        public static PagedList<T> ToPagedList(IEnumerable<T> data, int count, int pageNumber, int pageSize) => new PagedList<T>(data.ToList(), count, pageNumber, pageSize);
    }
}
