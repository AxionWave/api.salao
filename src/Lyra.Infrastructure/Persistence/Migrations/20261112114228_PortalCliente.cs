using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Lyra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PortalCliente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "portal",
                schema: "lyra",
                table: "empresa_config",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "portal_endereco",
                schema: "lyra",
                table: "empresa_config",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "codigos_acesso",
                schema: "lyra",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    email = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    empresa_id = table.Column<int>(type: "integer", nullable: false),
                    codigo_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expira = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    tentativas = table.Column<int>(type: "integer", nullable: false),
                    usado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    data_criacao = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_codigos_acesso", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "contas_cliente",
                schema: "lyra",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    email = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    telefone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    data_criacao = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ultimo_acesso = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contas_cliente", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_empresa_config_portal_endereco",
                schema: "lyra",
                table: "empresa_config",
                column: "portal_endereco",
                unique: true,
                filter: "portal_endereco IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_codigos_acesso_email",
                schema: "lyra",
                table: "codigos_acesso",
                columns: new[] { "email", "data_criacao" });

            migrationBuilder.CreateIndex(
                name: "ix_codigos_acesso_ip",
                schema: "lyra",
                table: "codigos_acesso",
                columns: new[] { "ip", "data_criacao" });

            migrationBuilder.CreateIndex(
                name: "ux_contas_cliente_email",
                schema: "lyra",
                table: "contas_cliente",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "codigos_acesso",
                schema: "lyra");

            migrationBuilder.DropTable(
                name: "contas_cliente",
                schema: "lyra");

            migrationBuilder.DropIndex(
                name: "ux_empresa_config_portal_endereco",
                schema: "lyra",
                table: "empresa_config");

            migrationBuilder.DropColumn(
                name: "portal",
                schema: "lyra",
                table: "empresa_config");

            migrationBuilder.DropColumn(
                name: "portal_endereco",
                schema: "lyra",
                table: "empresa_config");
        }
    }
}
