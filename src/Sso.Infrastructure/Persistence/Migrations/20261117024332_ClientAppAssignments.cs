using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sso.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientAppAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "clients",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "launch_url",
                table: "clients",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "require_assignment",
                table: "clients",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "client_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_client_assignments_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_client_assignments_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_client_assignments_client_id",
                table: "client_assignments",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_assignments_user_id",
                table: "client_assignments",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_assignments_user_id_client_id",
                table: "client_assignments",
                columns: new[] { "user_id", "client_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "client_assignments");

            migrationBuilder.DropColumn(
                name: "description",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "launch_url",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "require_assignment",
                table: "clients");
        }
    }
}
