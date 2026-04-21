using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kun.Sample.Catalog.Infrastructure.Migrations;

/// <inheritdoc />
public partial class Initial_SchemaQualified : Migration
{
    private static readonly string[] s_outboxStatusOccurredAt = ["Status", "OccurredAt"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "catalog");

        migrationBuilder.CreateTable(
            name: "catalog_products",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                Price = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                Stock = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                UpdatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_catalog_products", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                EventTypeId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Payload = table.Column<string>(type: "text", nullable: false),
                TraceParent = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                TraceState = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                AttemptCount = table.Column<int>(type: "integer", nullable: false),
                LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_outbox_messages", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_catalog_products_category_id",
            schema: "catalog",
            table: "catalog_products",
            column: "CategoryId");

        migrationBuilder.CreateIndex(
            name: "IX_catalog_products_is_deleted",
            schema: "catalog",
            table: "catalog_products",
            column: "IsDeleted");

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_status_occurredAt",
            schema: "catalog",
            table: "outbox_messages",
            columns: s_outboxStatusOccurredAt);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "catalog_products",
            schema: "catalog");

        migrationBuilder.DropTable(
            name: "outbox_messages",
            schema: "catalog");
    }
}
