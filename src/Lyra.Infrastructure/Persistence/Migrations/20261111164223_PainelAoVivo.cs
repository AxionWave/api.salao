using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lyra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PainelAoVivo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "painel_leituras",
                schema: "lyra",
                columns: table => new
                {
                    empresa_id = table.Column<int>(type: "integer", nullable: false),
                    usuario_id = table.Column<int>(type: "integer", nullable: false),
                    visto_ate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_painel_leituras", x => new { x.empresa_id, x.usuario_id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_notificacoes_painel",
                schema: "lyra",
                table: "notificacoes",
                columns: new[] { "empresa_id", "data_criacao" },
                filter: "canal = 'painel'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "painel_leituras",
                schema: "lyra");

            migrationBuilder.DropIndex(
                name: "ix_notificacoes_painel",
                schema: "lyra",
                table: "notificacoes");
        }
    }
}
