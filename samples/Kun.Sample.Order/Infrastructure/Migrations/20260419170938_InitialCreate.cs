using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Kun.Sample.Order.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    private static readonly string[] s_outboxStatusOccurredAt = ["Status", "OccurredAt"];
    private static readonly string[] s_sagaInstancesStateTimeoutAt = ["State", "TimeoutAt"];
    private static readonly string[] s_sagaStepQueueProcessedEnqueued = ["ProcessedAt", "EnqueuedAt"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "orders",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                total_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                cancellation_reason = table.Column<string>(type: "text", nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                UpdatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_orders", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "outbox_messages",
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

        migrationBuilder.CreateTable(
            name: "saga_instances",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SagaName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                ContextPayload = table.Column<string>(type: "text", nullable: false),
                CurrentStepIndex = table.Column<int>(type: "integer", nullable: false),
                State = table.Column<int>(type: "integer", nullable: false),
                StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                TimeoutAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                FailureReason = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                TraceId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_saga_instances", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "saga_step_queue",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SagaId = table.Column<Guid>(type: "uuid", nullable: false),
                StepIndex = table.Column<int>(type: "integer", nullable: false),
                Direction = table.Column<int>(type: "integer", nullable: false),
                EnqueuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                AttemptCount = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_saga_step_queue", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "order_lines",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                product_id = table.Column<Guid>(type: "uuid", nullable: false),
                quantity = table.Column<int>(type: "integer", nullable: false),
                unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                order_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_order_lines", x => x.id);
                table.ForeignKey(
                    name: "FK_order_lines_orders_order_id",
                    column: x => x.order_id,
                    principalTable: "orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_order_lines_order_id",
            table: "order_lines",
            column: "order_id");

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_status_occurredAt",
            table: "outbox_messages",
            columns: s_outboxStatusOccurredAt);

        migrationBuilder.CreateIndex(
            name: "ix_saga_instances_saga_name",
            table: "saga_instances",
            column: "SagaName");

        migrationBuilder.CreateIndex(
            name: "ix_saga_instances_state_timeout_at",
            table: "saga_instances",
            columns: s_sagaInstancesStateTimeoutAt);

        migrationBuilder.CreateIndex(
            name: "ix_saga_step_queue_processed_at_enqueued_at",
            table: "saga_step_queue",
            columns: s_sagaStepQueueProcessedEnqueued);

        migrationBuilder.CreateIndex(
            name: "ix_saga_step_queue_saga_id",
            table: "saga_step_queue",
            column: "SagaId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "order_lines");

        migrationBuilder.DropTable(
            name: "outbox_messages");

        migrationBuilder.DropTable(
            name: "saga_instances");

        migrationBuilder.DropTable(
            name: "saga_step_queue");

        migrationBuilder.DropTable(
            name: "orders");
    }
}
