using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Linq.Expressions;

namespace BuildDependencyReader.Common
{
    public static class EnumerableExtensions
    {
        /// <summary>
        /// Splits the given enumerable into two enumerables, based on whether or not each item matches the given <paramref name="predicate"/>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="enumerable"></param>
        /// <param name="predicate"></param>
        /// <returns>A pair of enumerables: enumerable in key = matching items, value = non-matching items.</returns>
        public static KeyValuePair<IEnumerable<T>, IEnumerable<T>> Split<T>(this IEnumerable<T> enumerable, Func<T, bool> predicate)
        {
            List<T> match = new List<T>();
            List<T> dontMatch = new List<T>();
            foreach (var item in enumerable)
            {
                if (predicate(item))
                {
                    match.Add(item);
                }
                else
                {
                    dontMatch.Add(item);
                }
            }
            return new KeyValuePair<IEnumerable<T>, IEnumerable<T>>(match, dontMatch);
        }
    }
}
