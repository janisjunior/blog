using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WheelContentManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SafetyInvariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Articles_GalleryId",
                table: "Articles");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Brands_Code",
                table: "Brands",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_Articles_GalleryId",
                table: "Articles",
                column: "GalleryId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Galleries_Brands_Brand",
                table: "Galleries",
                column: "Brand",
                principalTable: "Brands",
                principalColumn: "Code",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Galleries_Brands_Brand",
                table: "Galleries");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Brands_Code",
                table: "Brands");

            migrationBuilder.DropIndex(
                name: "IX_Articles_GalleryId",
                table: "Articles");

            migrationBuilder.CreateIndex(
                name: "IX_Articles_GalleryId",
                table: "Articles",
                column: "GalleryId");
        }
    }
}
