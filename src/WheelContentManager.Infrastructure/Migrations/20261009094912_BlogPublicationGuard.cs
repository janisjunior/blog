using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WheelContentManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BlogPublicationGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BlogBlocked",
                table: "Galleries",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "BlogCheckedAt",
                table: "Galleries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BlogStatus",
                table: "Galleries",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExistingBlogUrl",
                table: "Galleries",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BlogBlocked",
                table: "Galleries");

            migrationBuilder.DropColumn(
                name: "BlogCheckedAt",
                table: "Galleries");

            migrationBuilder.DropColumn(
                name: "BlogStatus",
                table: "Galleries");

            migrationBuilder.DropColumn(
                name: "ExistingBlogUrl",
                table: "Galleries");
        }
    }
}
