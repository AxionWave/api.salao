using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Lyra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateProfissionais : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "profissionais",
                schema: "lyra",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    empresa_id = table.Column<int>(type: "integer", nullable: false),
                    pessoa_id = table.Column<int>(type: "integer", nullable: false),
                    apelido = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    cor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    data_criacao = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    data_atualizacao = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_profissionais", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "disponibilidades",
                schema: "lyra",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    profissional_id = table.Column<int>(type: "integer", nullable: false),
                    dia_semana = table.Column<int>(type: "integer", nullable: false),
                    inicio = table.Column<TimeOnly>(type: "time", nullable: false),
                    fim = table.Column<TimeOnly>(type: "time", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_disponibilidades", x => x.id);
                    table.ForeignKey(
                        name: "FK_disponibilidades_profissionais_profissional_id",
                        column: x => x.profissional_id,
                        principalSchema: "lyra",
                        principalTable: "profissionais",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_profissionais_empresa_id",
                schema: "lyra",
                table: "profissionais",
                column: "empresa_id");

            migrationBuilder.CreateIndex(
                name: "ux_profissionais_empresa_pessoa",
                schema: "lyra",
                table: "profissionais",
                columns: new[] { "empresa_id", "pessoa_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_profissionais_empresa_apelido",
                schema: "lyra",
                table: "profissionais",
                columns: new[] { "empresa_id", "apelido" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_disponibilidades_profissional_id",
                schema: "lyra",
                table: "disponibilidades",
                column: "profissional_id");

            migrationBuilder.CreateIndex(
                name: "ux_disponibilidades_profissional_dia",
                schema: "lyra",
                table: "disponibilidades",
                columns: new[] { "profissional_id", "dia_semana" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "disponibilidades",
                schema: "lyra");

            migrationBuilder.DropTable(
                name: "profissionais",
                schema: "lyra");
        }
    }
}
