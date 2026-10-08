using Kun.Audit.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Kun.Auditing;

/// <summary>先登记审计表，再调用宿主模型配置，让 KunDbContext 对审计表应用租户过滤。</summary>
internal sealed class AuditingModelCustomizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        modelBuilder.ApplyConfiguration(new AuditLogConfiguration());
        base.Customize(modelBuilder, context);
    }
}
