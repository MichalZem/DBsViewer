using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbsViewer.SampleMigrations.Migrations
{
    /// <inheritdoc />
    public partial class ZakladniModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Autori",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Jmeno = table.Column<string>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Autori", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Clanky",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nadpis = table.Column<string>(type: "TEXT", nullable: false),
                    AutorId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clanky", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Clanky_Autori_AutorId",
                        column: x => x.AutorId,
                        principalTable: "Autori",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Autori_Email",
                table: "Autori",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clanky_AutorId",
                table: "Clanky",
                column: "AutorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Clanky");

            migrationBuilder.DropTable(
                name: "Autori");
        }
    }
}
