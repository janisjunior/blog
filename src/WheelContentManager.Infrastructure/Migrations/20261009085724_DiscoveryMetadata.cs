using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WheelContentManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DiscoveryMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Manual",
                table: "SourceReferences",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DetailsLoaded",
                table: "Galleries",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ListingOrder",
                table: "Galleries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailUrl",
                table: "Galleries",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Manual",
                table: "SourceReferences");

            migrationBuilder.DropColumn(
                name: "DetailsLoaded",
                table: "Galleries");

            migrationBuilder.DropColumn(
                name: "ListingOrder",
                table: "Galleries");

            migrationBuilder.DropColumn(
                name: "ThumbnailUrl",
                table: "Galleries");
        }
    }
}
