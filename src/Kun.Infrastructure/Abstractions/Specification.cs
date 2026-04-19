using System.Linq.Expressions;

namespace Kun.Infrastructure.Abstractions;

/// <summary>
/// 规约模式默认实现。业务方通过继承并在构造器中调用 <c>AddCriteria</c> / <c>ApplyOrderBy</c> /
/// <c>ApplyPaging</c> 等保护方法来定义查询语义。
/// </summary>
public abstract class Specification<T> : ISpecification<T>
{
    private readonly List<string> _includeStrings = new();

    /// <inheritdoc/>
    public Expression<Func<T, bool>>? Criteria { get; private set; }
    /// <inheritdoc/>
    public Expression<Func<T, object>>? OrderBy { get; private set; }
    /// <inheritdoc/>
    public Expression<Func<T, object>>? OrderByDescending { get; private set; }
    /// <inheritdoc/>
    public int Skip { get; private set; }
    /// <inheritdoc/>
    public int Take { get; private set; }
    /// <inheritdoc/>
    public IReadOnlyList<string> IncludeStrings => _includeStrings;

    /// <inheritdoc/>
    protected void AddCriteria(Expression<Func<T, bool>> criteria)
    {
        // 多次调用时以 AndAlso 合并
        if (Criteria is null)
        {
            Criteria = criteria;
            return;
        }
        var param = Expression.Parameter(typeof(T));
        var body = Expression.AndAlso(
            Expression.Invoke(Criteria, param),
            Expression.Invoke(criteria, param));
        Criteria = Expression.Lambda<Func<T, bool>>(body, param);
    }

    /// <inheritdoc/>
    protected void ApplyOrderBy(Expression<Func<T, object>> orderBy) => OrderBy = orderBy;
    /// <inheritdoc/>
    protected void ApplyOrderByDescending(Expression<Func<T, object>> orderByDesc) => OrderByDescending = orderByDesc;

    /// <inheritdoc/>
    protected void ApplyPaging(int skip, int take)
    {
        Skip = skip;
        Take = take;
    }

    /// <inheritdoc/>
    protected void AddInclude(string includeString) => _includeStrings.Add(includeString);
}
