using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Municipal.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class CommunityFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "StatusHistory",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResidentReview",
                table: "Requests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ResolutionNotes",
                table: "Requests",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ResolvedAt",
                table: "Requests",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RequestPhotos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProblemRequestId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    IsResolution = table.Column<bool>(type: "bit", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequestPhotos_Requests_ProblemRequestId",
                        column: x => x.ProblemRequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RequestSupporters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProblemRequestId = table.Column<int>(type: "int", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    EmailKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestSupporters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequestSupporters_Requests_ProblemRequestId",
                        column: x => x.ProblemRequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Requests_MunicipalityCode_Status_ReportedAt",
                table: "Requests",
                columns: new[] { "MunicipalityCode", "Status", "ReportedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RequestPhotos_ProblemRequestId",
                table: "RequestPhotos",
                column: "ProblemRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestSupporters_ProblemRequestId_EmailKey",
                table: "RequestSupporters",
                columns: new[] { "ProblemRequestId", "EmailKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RequestPhotos");

            migrationBuilder.DropTable(
                name: "RequestSupporters");

            migrationBuilder.DropIndex(
                name: "IX_Requests_MunicipalityCode_Status_ReportedAt",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "StatusHistory");

            migrationBuilder.DropColumn(
                name: "ResidentReview",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "ResolutionNotes",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "ResolvedAt",
                table: "Requests");
        }
    }
}
