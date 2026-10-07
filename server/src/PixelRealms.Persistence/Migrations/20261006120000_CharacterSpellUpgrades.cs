using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PixelRealms.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CharacterSpellUpgrades : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "character_spell_upgrades",
                columns: table => new
                {
                    character_id = table.Column<Guid>(type: "uuid", nullable: false),
                    spell_id = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    upgrade_id = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_character_spell_upgrades", x => new { x.character_id, x.spell_id });
                    table.ForeignKey(
                        name: "fk_character_spell_upgrades_characters_character_id",
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
                name: "character_spell_upgrades");
        }
    }
}
