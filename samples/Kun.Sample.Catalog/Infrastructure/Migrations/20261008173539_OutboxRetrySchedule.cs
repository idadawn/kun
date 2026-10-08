using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kun.Sample.Catalog.Infrastructure.Migrations;
/// <inheritdoc />
public partial class OutboxRetrySchedule : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "NextAttemptAt",
            schema: "catalog",
            table: "outbox_messages",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "NextAttemptAt",
            schema: "catalog",
            table: "outbox_messages");
    }
}
