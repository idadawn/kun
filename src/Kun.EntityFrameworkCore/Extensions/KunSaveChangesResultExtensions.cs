using Kun.Core.Results;
using Microsoft.EntityFrameworkCore;

namespace Kun.EntityFrameworkCore.Extensions;

/// <summary>在 Result 边界显式处理乐观并发冲突，其他系统异常仍抛出。</summary>
public static class KunSaveChangesResultExtensions
{
    /// <summary>xmin 冲突映射为 Conflict，不自动重试或覆盖其他写入者。</summary>
    public static async Task<Result<int>> SaveChangesResultAsync(this DbContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            return Result<int>.Ok(await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<int>.Fail(Error.Conflict("data.concurrency_conflict", "数据已被其他操作修改，请重新读取后再提交。"));
        }
    }
}
