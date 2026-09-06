using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lyra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProfissionaisCadastroDireto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "pessoa_id",
                schema: "lyra",
                table: "profissionais",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "telefone",
                schema: "lyra",
                table: "profissionais",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "apelido",
                schema: "lyra",
                table: "profissionais",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.DropIndex(
                name: "ux_profissionais_empresa_pessoa",
                schema: "lyra",
                table: "profissionais");

            migrationBuilder.CreateIndex(
                name: "ux_profissionais_empresa_pessoa",
                schema: "lyra",
                table: "profissionais",
                columns: new[] { "empresa_id", "pessoa_id" },
                unique: true,
                filter: "pessoa_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_profissionais_empresa_pessoa",
                schema: "lyra",
                table: "profissionais");

            migrationBuilder.DropColumn(
                name: "telefone",
                schema: "lyra",
                table: "profissionais");

            migrationBuilder.AlterColumn<string>(
                name: "apelido",
                schema: "lyra",
                table: "profissionais",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120);

            migrationBuilder.AlterColumn<int>(
                name: "pessoa_id",
                schema: "lyra",
                table: "profissionais",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_profissionais_empresa_pessoa",
                schema: "lyra",
                table: "profissionais",
                columns: new[] { "empresa_id", "pessoa_id" },
                unique: true);
        }
    }
}
