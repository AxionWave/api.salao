using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Lyra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FamiliasDeClientes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_clientes_empresa_email",
                schema: "lyra",
                table: "clientes");

            migrationBuilder.DropIndex(
                name: "ux_clientes_empresa_telefone",
                schema: "lyra",
                table: "clientes");

            migrationBuilder.AddColumn<int>(
                name: "familia_id",
                schema: "lyra",
                table: "clientes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "parentesco",
                schema: "lyra",
                table: "clientes",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "familias",
                schema: "lyra",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    empresa_id = table.Column<int>(type: "integer", nullable: false),
                    nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    data_criacao = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_familias", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_clientes_empresa_email",
                schema: "lyra",
                table: "clientes",
                columns: new[] { "empresa_id", "email" },
                filter: "email IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_clientes_empresa_telefone",
                schema: "lyra",
                table: "clientes",
                columns: new[] { "empresa_id", "telefone" });

            migrationBuilder.CreateIndex(
                name: "ix_clientes_familia_id",
                schema: "lyra",
                table: "clientes",
                column: "familia_id");

            migrationBuilder.CreateIndex(
                name: "ix_familias_empresa_id",
                schema: "lyra",
                table: "familias",
                column: "empresa_id");

            migrationBuilder.AddForeignKey(
                name: "FK_clientes_familias_familia_id",
                schema: "lyra",
                table: "clientes",
                column: "familia_id",
                principalSchema: "lyra",
                principalTable: "familias",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_clientes_familias_familia_id",
                schema: "lyra",
                table: "clientes");

            migrationBuilder.DropTable(
                name: "familias",
                schema: "lyra");

            migrationBuilder.DropIndex(
                name: "ix_clientes_empresa_email",
                schema: "lyra",
                table: "clientes");

            migrationBuilder.DropIndex(
                name: "ix_clientes_empresa_telefone",
                schema: "lyra",
                table: "clientes");

            migrationBuilder.DropIndex(
                name: "ix_clientes_familia_id",
                schema: "lyra",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "familia_id",
                schema: "lyra",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "parentesco",
                schema: "lyra",
                table: "clientes");

            migrationBuilder.CreateIndex(
                name: "ux_clientes_empresa_email",
                schema: "lyra",
                table: "clientes",
                columns: new[] { "empresa_id", "email" },
                unique: true,
                filter: "email IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_clientes_empresa_telefone",
                schema: "lyra",
                table: "clientes",
                columns: new[] { "empresa_id", "telefone" },
                unique: true);
        }
    }
}
