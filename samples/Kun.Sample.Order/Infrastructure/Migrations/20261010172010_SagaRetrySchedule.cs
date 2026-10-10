using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kun.Sample.Order.Infrastructure.Migrations;

/// <inheritdoc />
public partial class SagaRetrySchedule : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "NextAttemptAt",
            schema: "order",
            table: "saga_step_queue",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "NextAttemptAt",
            schema: "order",
            table: "saga_step_queue");
    }
}
