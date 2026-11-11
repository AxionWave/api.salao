using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lyra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LembretesEResumoDiario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int[]>(
                name: "lembretes_horas",
                schema: "lyra",
                table: "empresa_config",
                type: "integer[]",
                nullable: false,
                defaultValueSql: "'{24}'::integer[]");

            migrationBuilder.AddColumn<bool>(
                name: "resumo_diario",
                schema: "lyra",
                table: "empresa_config",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "avisos_programados",
                schema: "lyra",
                columns: table => new
                {
                    chave = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    empresa_id = table.Column<int>(type: "integer", nullable: false),
                    data_criacao = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_avisos_programados", x => x.chave);
                });

            migrationBuilder.CreateIndex(
                name: "ix_avisos_programados_data",
                schema: "lyra",
                table: "avisos_programados",
                column: "data_criacao");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "avisos_programados",
                schema: "lyra");

            migrationBuilder.DropColumn(
                name: "lembretes_horas",
                schema: "lyra",
                table: "empresa_config");

            migrationBuilder.DropColumn(
                name: "resumo_diario",
                schema: "lyra",
                table: "empresa_config");
        }
    }
}
