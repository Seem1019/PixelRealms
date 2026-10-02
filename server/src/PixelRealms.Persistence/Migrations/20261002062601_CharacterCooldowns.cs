using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelRealms.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CharacterCooldowns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "character_cooldowns",
                columns: table => new
                {
                    character_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    @ref = table.Column<string>(name: "ref", type: "character varying(48)", maxLength: 48, nullable: false),
                    ends_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_character_cooldowns", x => new { x.character_id, x.kind, x.@ref });
                    table.ForeignKey(
                        name: "fk_character_cooldowns_characters_character_id",
                        column: x => x.character_id,
                        principalTable: "characters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "character_cooldowns");
        }
    }
}
