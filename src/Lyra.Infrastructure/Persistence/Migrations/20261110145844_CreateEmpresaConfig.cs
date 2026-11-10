using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lyra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateEmpresaConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "empresa_config",
                schema: "lyra",
                columns: table => new
                {
                    empresa_id = table.Column<int>(type: "integer", nullable: false),
                    segmento = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    fuso_horario = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    terminologia = table.Column<string>(type: "jsonb", nullable: true),
                    data_criacao = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    data_atualizacao = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_empresa_config", x => x.empresa_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "empresa_config",
                schema: "lyra");
        }
    }
}
