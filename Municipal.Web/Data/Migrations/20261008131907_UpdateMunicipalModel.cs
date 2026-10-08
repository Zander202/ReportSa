using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Municipal.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdateMunicipalModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResidentEmail",
                table: "Requests",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResidentEmail",
                table: "Requests");
        }
    }
}
