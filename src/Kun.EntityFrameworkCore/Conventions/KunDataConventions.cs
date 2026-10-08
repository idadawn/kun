using System.Text;
using Kun.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Kun.EntityFrameworkCore.Conventions;

/// <summary>数据层的列命名、Guid v7 主键与 PostgreSQL xmin 并发约定。</summary>
public static class KunDataConventions
{
    /// <summary>EF shadow property 名称，映射到 PostgreSQL 系统列 xmin。</summary>
    public const string RowVersionPropertyName = "KunRowVersion";

    /// <summary>在实体映射完成后应用约定；已有非空主键保持不变。</summary>
    public static ModelBuilder ApplyKunDataConventions(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        foreach (var entity in modelBuilder.Model.GetEntityTypes().ToArray())
        {
            if (entity.BaseType is not null || entity.IsOwned()) continue;
            var builder = modelBuilder.Entity(entity.ClrType);
            if (entity.GetTableName() is { } table) entity.SetTableName(ToSnakeCase(table));
            var key = entity.FindPrimaryKey();
            if (key?.Properties.Count == 1 && key.Properties[0].ClrType == typeof(Guid))
                builder.Property<Guid>(key.Properties[0].Name).ValueGeneratedOnAdd()
                    .HasValueGenerator<KunGuidV7ValueGenerator>();

            if (typeof(IAuditedObject).IsAssignableFrom(entity.ClrType))
            {
                foreach (var name in new[] { "CreatedBy", "UpdatedBy" })
                {
                    var property = entity.FindProperty(name);
                    if (property is null) continue;
                    if (property.ClrType.IsValueType && Nullable.GetUnderlyingType(property.ClrType) is null)
                        throw new InvalidOperationException(
                            $"{entity.ClrType.Name}.{name} 必须可空；请使用 AggregateRoot<TKey, Guid?> 或可空的数值用户键。");
                    builder.Property(name).IsRequired(false);
                }
                builder.Property<uint>(RowVersionPropertyName).IsRowVersion().HasColumnName("xmin");
            }
            foreach (var property in entity.GetProperties())
            {
                if (property.Name != RowVersionPropertyName)
                    property.SetColumnName(ToSnakeCase(property.GetColumnName()));
            }
            foreach (var index in entity.GetIndexes())
                if (index.GetDatabaseName() is { } indexName) index.SetDatabaseName(ToSnakeCase(indexName));
            foreach (var entityKey in entity.GetKeys())
                if (entityKey.GetName() is { } keyName) entityKey.SetName(ToSnakeCase(keyName));
        }
        return modelBuilder;
    }

    private static string ToSnakeCase(string name)
    {
        var result = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0 && name[i - 1] != '_' &&
                (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                result.Append('_');
            result.Append(char.ToLowerInvariant(c));
        }
        return result.ToString();
    }
}

/// <summary>只为未赋值的 Guid 主键生成有时间顺序的版本 7 标识。</summary>
public sealed class KunGuidV7ValueGenerator : ValueGenerator<Guid>
{
    /// <inheritdoc />
    public override bool GeneratesTemporaryValues => false;
    /// <inheritdoc />
    public override Guid Next(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry) => Guid.CreateVersion7();
}
