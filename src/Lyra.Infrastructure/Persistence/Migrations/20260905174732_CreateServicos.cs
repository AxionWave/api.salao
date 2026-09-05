using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Lyra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateServicos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "lyra");

            migrationBuilder.CreateTable(
                name: "servicos",
                schema: "lyra",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    empresa_id = table.Column<int>(type: "integer", nullable: false),
                    nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    duracao_minutos = table.Column<int>(type: "integer", nullable: false),
                    preco = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    categoria = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    data_criacao = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    data_atualizacao = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_servicos", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_servicos_empresa_id",
                schema: "lyra",
                table: "servicos",
                column: "empresa_id");

            migrationBuilder.CreateIndex(
                name: "ux_servicos_empresa_nome",
                schema: "lyra",
                table: "servicos",
                columns: new[] { "empresa_id", "nome" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "servicos",
                schema: "lyra");
        }
    }
}
