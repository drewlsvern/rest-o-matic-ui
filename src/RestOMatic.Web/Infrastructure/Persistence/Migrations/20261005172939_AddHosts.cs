using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestOMatic.Web.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Hosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    EnrolTokenHash = table.Column<byte[]>(type: "BLOB", nullable: true),
                    EnrolTokenExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CredentialHash = table.Column<byte[]>(type: "BLOB", nullable: true),
                    PublicKey = table.Column<string>(type: "TEXT", nullable: true),
                    EnrolledAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Hostname = table.Column<string>(type: "TEXT", nullable: true),
                    Os = table.Column<string>(type: "TEXT", nullable: true),
                    Arch = table.Column<string>(type: "TEXT", nullable: true),
                    RestOMaticVersion = table.Column<string>(type: "TEXT", nullable: true),
                    ResticVersion = table.Column<string>(type: "TEXT", nullable: true),
                    LastCheckInAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    JobCount = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hosts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HostParts",
                columns: table => new
                {
                    HostId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    IsCurrent = table.Column<bool>(type: "INTEGER", nullable: false),
                    WithheldReason = table.Column<string>(type: "TEXT", nullable: true),
                    WithheldFields = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostParts", x => new { x.HostId, x.Kind });
                    table.ForeignKey(
                        name: "FK_HostParts_Hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "Hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Hosts_CredentialHash",
                table: "Hosts",
                column: "CredentialHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Hosts_EnrolTokenHash",
                table: "Hosts",
                column: "EnrolTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Hosts_NormalizedName",
                table: "Hosts",
                column: "NormalizedName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HostParts");

            migrationBuilder.DropTable(
                name: "Hosts");
        }
    }
}
