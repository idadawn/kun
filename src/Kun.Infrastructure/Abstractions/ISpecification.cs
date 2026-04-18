using System.Linq.Expressions;

namespace Kun.Infrastructure.Abstractions;

/// <summary>
/// 规约模式（Specification Pattern）抽象。
/// 把查询条件、排序、分页从仓储中剥离出来，以可组合的对象表达业务意图。
/// </summary>
public interface ISpecification<T>
{
    /// <summary>查询条件谓词。</summary>
    Expression<Func<T, bool>>? Criteria { get; }

    /// <summary>排序表达式（升序）。</summary>
    Expression<Func<T, object>>? OrderBy { get; }

    /// <summary>排序表达式（降序）。</summary>
    Expression<Func<T, object>>? OrderByDescending { get; }

    /// <summary>分页：Skip 数量。</summary>
    int Skip { get; }

    /// <summary>分页：Take 数量（0 表示不分页）。</summary>
    int Take { get; }

    /// <summary>需要 Include 的导航属性字符串形式（如 <c>"Lines.Product"</c>）。</summary>
    IReadOnlyList<string> IncludeStrings { get; }
}
