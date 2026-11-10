using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lyra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemarcacaoEMotivoCancelamento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "motivo_cancelamento",
                schema: "lyra",
                table: "agendamentos",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "remarcado_de_id",
                schema: "lyra",
                table: "agendamentos",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_agendamentos_remarcado_de_id",
                schema: "lyra",
                table: "agendamentos",
                column: "remarcado_de_id");

            migrationBuilder.AddForeignKey(
                name: "FK_agendamentos_agendamentos_remarcado_de_id",
                schema: "lyra",
                table: "agendamentos",
                column: "remarcado_de_id",
                principalSchema: "lyra",
                principalTable: "agendamentos",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_agendamentos_agendamentos_remarcado_de_id",
                schema: "lyra",
                table: "agendamentos");

            migrationBuilder.DropIndex(
                name: "ix_agendamentos_remarcado_de_id",
                schema: "lyra",
                table: "agendamentos");

            migrationBuilder.DropColumn(
                name: "motivo_cancelamento",
                schema: "lyra",
                table: "agendamentos");

            migrationBuilder.DropColumn(
                name: "remarcado_de_id",
                schema: "lyra",
                table: "agendamentos");
        }
    }
}
