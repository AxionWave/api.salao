using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Lyra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgendamentoVariosServicos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agendamento_itens",
                schema: "lyra",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    agendamento_id = table.Column<int>(type: "integer", nullable: false),
                    servico_id = table.Column<int>(type: "integer", nullable: false),
                    ordem = table.Column<int>(type: "integer", nullable: false),
                    duracao_minutos = table.Column<int>(type: "integer", nullable: false),
                    preco = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agendamento_itens", x => x.id);
                    table.ForeignKey(
                        name: "FK_agendamento_itens_agendamentos_agendamento_id",
                        column: x => x.agendamento_id,
                        principalSchema: "lyra",
                        principalTable: "agendamentos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agendamento_itens_servicos_servico_id",
                        column: x => x.servico_id,
                        principalSchema: "lyra",
                        principalTable: "servicos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO lyra.agendamento_itens (agendamento_id, servico_id, ordem, duracao_minutos, preco)
                SELECT a.id, a.servico_id, 0, s.duracao_minutos, s.preco
                FROM lyra.agendamentos a
                INNER JOIN lyra.servicos s ON s.id = a.servico_id;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_agendamentos_servicos_servico_id",
                schema: "lyra",
                table: "agendamentos");

            migrationBuilder.DropIndex(
                name: "IX_agendamentos_servico_id",
                schema: "lyra",
                table: "agendamentos");

            migrationBuilder.DropColumn(
                name: "servico_id",
                schema: "lyra",
                table: "agendamentos");

            migrationBuilder.CreateIndex(
                name: "ix_agendamento_itens_agendamento_id",
                schema: "lyra",
                table: "agendamento_itens",
                column: "agendamento_id");

            migrationBuilder.CreateIndex(
                name: "ix_agendamento_itens_agendamento_ordem",
                schema: "lyra",
                table: "agendamento_itens",
                columns: new[] { "agendamento_id", "ordem" });

            migrationBuilder.CreateIndex(
                name: "IX_agendamento_itens_servico_id",
                schema: "lyra",
                table: "agendamento_itens",
                column: "servico_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "servico_id",
                schema: "lyra",
                table: "agendamentos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                UPDATE lyra.agendamentos a
                SET servico_id = i.servico_id
                FROM (
                    SELECT DISTINCT ON (agendamento_id) agendamento_id, servico_id
                    FROM lyra.agendamento_itens
                    ORDER BY agendamento_id, ordem, id
                ) i
                WHERE a.id = i.agendamento_id;
                """);

            migrationBuilder.DropTable(
                name: "agendamento_itens",
                schema: "lyra");

            migrationBuilder.CreateIndex(
                name: "IX_agendamentos_servico_id",
                schema: "lyra",
                table: "agendamentos",
                column: "servico_id");

            migrationBuilder.AddForeignKey(
                name: "FK_agendamentos_servicos_servico_id",
                schema: "lyra",
                table: "agendamentos",
                column: "servico_id",
                principalSchema: "lyra",
                principalTable: "servicos",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
