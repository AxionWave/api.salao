using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Lyra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExpedienteFlexivelEBloqueios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_disponibilidades_profissional_dia",
                schema: "lyra",
                table: "disponibilidades");

            migrationBuilder.CreateTable(
                name: "bloqueios",
                schema: "lyra",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    empresa_id = table.Column<int>(type: "integer", nullable: false),
                    profissional_id = table.Column<int>(type: "integer", nullable: true),
                    inicio = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fim = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    motivo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    data_criacao = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bloqueios", x => x.id);
                    table.ForeignKey(
                        name: "FK_bloqueios_profissionais_profissional_id",
                        column: x => x.profissional_id,
                        principalSchema: "lyra",
                        principalTable: "profissionais",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_disponibilidades_profissional_dia",
                schema: "lyra",
                table: "disponibilidades",
                columns: new[] { "profissional_id", "dia_semana" });

            migrationBuilder.CreateIndex(
                name: "ix_bloqueios_empresa_periodo",
                schema: "lyra",
                table: "bloqueios",
                columns: new[] { "empresa_id", "inicio", "fim" });

            migrationBuilder.CreateIndex(
                name: "IX_bloqueios_profissional_id",
                schema: "lyra",
                table: "bloqueios",
                column: "profissional_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bloqueios",
                schema: "lyra");

            migrationBuilder.DropIndex(
                name: "ix_disponibilidades_profissional_dia",
                schema: "lyra",
                table: "disponibilidades");

            migrationBuilder.CreateIndex(
                name: "ux_disponibilidades_profissional_dia",
                schema: "lyra",
                table: "disponibilidades",
                columns: new[] { "profissional_id", "dia_semana" },
                unique: true);
        }
    }
}
