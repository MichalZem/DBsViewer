using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbsViewer.SampleMigrations.Migrations
{
    /// <inheritdoc />
    public partial class PridanoPublikovano : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "Publikovano",
                table: "Clanky",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Publikovano",
                table: "Clanky");
        }
    }
}
