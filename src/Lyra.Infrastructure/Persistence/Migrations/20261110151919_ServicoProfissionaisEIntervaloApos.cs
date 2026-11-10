using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lyra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServicoProfissionaisEIntervaloApos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "intervalo_apos_minutos",
                schema: "lyra",
                table: "servicos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "intervalo_apos_minutos",
                schema: "lyra",
                table: "agendamentos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "servico_profissionais",
                schema: "lyra",
                columns: table => new
                {
                    servico_id = table.Column<int>(type: "integer", nullable: false),
                    profissional_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_servico_profissionais", x => new { x.servico_id, x.profissional_id });
                    table.ForeignKey(
                        name: "FK_servico_profissionais_profissionais_profissional_id",
                        column: x => x.profissional_id,
                        principalSchema: "lyra",
                        principalTable: "profissionais",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_servico_profissionais_servicos_servico_id",
                        column: x => x.servico_id,
                        principalSchema: "lyra",
                        principalTable: "servicos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_servico_profissionais_profissional",
                schema: "lyra",
                table: "servico_profissionais",
                column: "profissional_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "servico_profissionais",
                schema: "lyra");

            migrationBuilder.DropColumn(
                name: "intervalo_apos_minutos",
                schema: "lyra",
                table: "servicos");

            migrationBuilder.DropColumn(
                name: "intervalo_apos_minutos",
                schema: "lyra",
                table: "agendamentos");
        }
    }
}
