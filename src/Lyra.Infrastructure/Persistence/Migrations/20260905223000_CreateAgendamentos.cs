using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Lyra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateAgendamentos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agendamentos",
                schema: "lyra",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    empresa_id = table.Column<int>(type: "integer", nullable: false),
                    cliente_id = table.Column<int>(type: "integer", nullable: false),
                    servico_id = table.Column<int>(type: "integer", nullable: false),
                    profissional_id = table.Column<int>(type: "integer", nullable: false),
                    inicio = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fim = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    observacoes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    data_criacao = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    data_atualizacao = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agendamentos", x => x.id);
                    table.ForeignKey(
                        name: "FK_agendamentos_clientes_cliente_id",
                        column: x => x.cliente_id,
                        principalSchema: "lyra",
                        principalTable: "clientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agendamentos_servicos_servico_id",
                        column: x => x.servico_id,
                        principalSchema: "lyra",
                        principalTable: "servicos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agendamentos_profissionais_profissional_id",
                        column: x => x.profissional_id,
                        principalSchema: "lyra",
                        principalTable: "profissionais",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agendamentos_empresa_id",
                schema: "lyra",
                table: "agendamentos",
                column: "empresa_id");

            migrationBuilder.CreateIndex(
                name: "ix_agendamentos_empresa_inicio",
                schema: "lyra",
                table: "agendamentos",
                columns: new[] { "empresa_id", "inicio" });

            migrationBuilder.CreateIndex(
                name: "ix_agendamentos_empresa_profissional_intervalo",
                schema: "lyra",
                table: "agendamentos",
                columns: new[] { "empresa_id", "profissional_id", "inicio", "fim" });

            migrationBuilder.CreateIndex(
                name: "IX_agendamentos_cliente_id",
                schema: "lyra",
                table: "agendamentos",
                column: "cliente_id");

            migrationBuilder.CreateIndex(
                name: "IX_agendamentos_servico_id",
                schema: "lyra",
                table: "agendamentos",
                column: "servico_id");

            migrationBuilder.CreateIndex(
                name: "IX_agendamentos_profissional_id",
                schema: "lyra",
                table: "agendamentos",
                column: "profissional_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agendamentos",
                schema: "lyra");
        }
    }
}
