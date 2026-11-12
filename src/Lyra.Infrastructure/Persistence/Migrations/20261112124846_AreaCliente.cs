using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lyra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AreaCliente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "conta_cliente_id",
                schema: "lyra",
                table: "agendamentos",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_agendamentos_conta_cliente_id",
                schema: "lyra",
                table: "agendamentos",
                column: "conta_cliente_id");

            migrationBuilder.AddForeignKey(
                name: "FK_agendamentos_contas_cliente_conta_cliente_id",
                schema: "lyra",
                table: "agendamentos",
                column: "conta_cliente_id",
                principalSchema: "lyra",
                principalTable: "contas_cliente",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_agendamentos_contas_cliente_conta_cliente_id",
                schema: "lyra",
                table: "agendamentos");

            migrationBuilder.DropIndex(
                name: "ix_agendamentos_conta_cliente_id",
                schema: "lyra",
                table: "agendamentos");

            migrationBuilder.DropColumn(
                name: "conta_cliente_id",
                schema: "lyra",
                table: "agendamentos");
        }
    }
}
