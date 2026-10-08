using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Municipal.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class RequestRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConfirmationEmailStatus",
                table: "Requests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EstimatedCompletionAt",
                table: "Requests",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "Requests",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "Requests",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MunicipalityCode",
                table: "Requests",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MunicipalityName",
                table: "Requests",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfirmationEmailStatus",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "EstimatedCompletionAt",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "MunicipalityCode",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "MunicipalityName",
                table: "Requests");
        }
    }
}
