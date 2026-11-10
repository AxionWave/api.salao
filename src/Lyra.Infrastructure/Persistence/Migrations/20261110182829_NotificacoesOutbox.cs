using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Lyra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotificacoesOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notificacoes",
                schema: "lyra",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    empresa_id = table.Column<int>(type: "integer", nullable: false),
                    evento = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    canal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    destino = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    cliente_id = table.Column<int>(type: "integer", nullable: true),
                    agendamento_id = table.Column<int>(type: "integer", nullable: true),
                    dados = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tentativas = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    proxima_tentativa = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ultimo_erro = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    provedor_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    data_criacao = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    data_envio = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notificacoes", x => x.id);
                    table.ForeignKey(
                        name: "FK_notificacoes_agendamentos_agendamento_id",
                        column: x => x.agendamento_id,
                        principalSchema: "lyra",
                        principalTable: "agendamentos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_notificacoes_clientes_cliente_id",
                        column: x => x.cliente_id,
                        principalSchema: "lyra",
                        principalTable: "clientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notificacoes_agendamento_id",
                schema: "lyra",
                table: "notificacoes",
                column: "agendamento_id");

            migrationBuilder.CreateIndex(
                name: "IX_notificacoes_cliente_id",
                schema: "lyra",
                table: "notificacoes",
                column: "cliente_id");

            migrationBuilder.CreateIndex(
                name: "ix_notificacoes_empresa_cliente",
                schema: "lyra",
                table: "notificacoes",
                columns: new[] { "empresa_id", "cliente_id", "data_criacao" });

            migrationBuilder.CreateIndex(
                name: "ix_notificacoes_pendentes",
                schema: "lyra",
                table: "notificacoes",
                column: "proxima_tentativa",
                filter: "status = 'pendente'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notificacoes",
                schema: "lyra");
        }
    }
}
