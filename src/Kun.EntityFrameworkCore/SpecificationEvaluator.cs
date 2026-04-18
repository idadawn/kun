using Kun.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Kun.EntityFrameworkCore;

/// <summary>
/// Applies an <see cref="ISpecification{T}"/> to an <see cref="IQueryable{T}"/>,
/// translating criteria, ordering, includes, and paging into EF Core LINQ operators.
/// </summary>
public static class SpecificationEvaluator
{
    /// <summary>
    /// Applies all clauses of <paramref name="spec"/> to <paramref name="query"/> and
    /// returns the resulting <see cref="IQueryable{T}"/>.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The base queryable to augment.</param>
    /// <param name="spec">The specification containing the filtering / sorting / paging rules.</param>
    /// <returns>A new <see cref="IQueryable{T}"/> with all specification clauses applied.</returns>
    public static IQueryable<T> Apply<T>(IQueryable<T> query, ISpecification<T> spec) where T : class
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(spec);

        if (spec.Criteria is not null)
            query = query.Where(spec.Criteria);

        foreach (var includeString in spec.IncludeStrings)
            query = query.Include(includeString);

        if (spec.OrderBy is not null)
            query = query.OrderBy(spec.OrderBy);
        else if (spec.OrderByDescending is not null)
            query = query.OrderByDescending(spec.OrderByDescending);

        if (spec.Take > 0)
            query = query.Skip(spec.Skip).Take(spec.Take);

        return query;
    }
}
