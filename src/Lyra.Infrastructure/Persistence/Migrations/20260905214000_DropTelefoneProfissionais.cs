using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lyra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropTelefoneProfissionais : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "telefone",
                schema: "lyra",
                table: "profissionais");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "telefone",
                schema: "lyra",
                table: "profissionais",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }
    }
}
