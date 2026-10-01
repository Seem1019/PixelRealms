// NO COMPILADO EN LA SESIÓN DE LA FASE 1 (NuGet bloqueado): revisar con `dotnet build` antes de confiar en él.
using Microsoft.EntityFrameworkCore;
using PixelRealms.Persistence.Entities;

namespace PixelRealms.Persistence.Ef;

/// <summary>Modelo de docs/database.md. Tablas y columnas en snake_case (UseSnakeCaseNamingConvention en el registro).</summary>
public sealed class GameDbContext(DbContextOptions<GameDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Character> Characters => Set<Character>();
    public DbSet<CharacterItem> CharacterItems => Set<CharacterItem>();
    public DbSet<CharacterHotbarSlot> CharacterHotbar => Set<CharacterHotbarSlot>();
    public DbSet<ItemAuditLog> ItemAuditLog => Set<ItemAuditLog>();

    /// <summary>Índices de unicidad; los repositorios traducen su violación (23505) a "nombre en uso".</summary>
    public const string AccountsUsernameIndex = "ix_accounts_username_ci";
    public const string CharactersNameIndex = "ix_characters_name_ci";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Nombres únicos sin distinguir mayúsculas: ICU nivel 2 ignora mayúsculas (no acentos); no determinista => "Bob" = "bob".
        modelBuilder.HasCollation("case_insensitive", locale: "und-u-ks-level2", provider: "icu", deterministic: false);
        modelBuilder.Entity<Account>(e =>
        {
            e.ToTable("accounts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Username).IsRequired().HasMaxLength(20).UseCollation("case_insensitive");
            e.Property(x => x.PasswordHash).IsRequired();
            e.HasIndex(x => x.Username).IsUnique().HasDatabaseName(AccountsUsernameIndex);
            e.HasMany(x => x.Characters).WithOne(c => c.Account).HasForeignKey(c => c.AccountId);
        });
        modelBuilder.Entity<Character>(e =>
        {
            e.ToTable("characters", t => t.HasCheckConstraint("ck_characters_level", "level BETWEEN 1 AND 15"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(64).UseCollation("case_insensitive");
            e.Property(x => x.ClassId).IsRequired().HasMaxLength(16);
            e.Property(x => x.MapId).IsRequired().HasMaxLength(32);
            e.HasIndex(x => x.Name).IsUnique().HasDatabaseName(CharactersNameIndex);
            e.HasIndex(x => x.AccountId);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.CharacterId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Hotbar).WithOne().HasForeignKey(h => h.CharacterId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<CharacterItem>(e =>
        {
            e.ToTable("character_items", t => t.HasCheckConstraint("ck_character_items_quantity", "quantity >= 1"));
            e.HasKey(x => x.Id);
            e.Property(x => x.TemplateId).IsRequired().HasMaxLength(48);
            e.HasIndex(x => new { x.CharacterId, x.Container, x.Slot }).IsUnique();
        });
        modelBuilder.Entity<CharacterHotbarSlot>(e =>
        {
            e.ToTable("character_hotbar");
            e.HasKey(x => new { x.CharacterId, x.Slot });
            e.Property(x => x.Ref).IsRequired().HasMaxLength(48);
        });
        modelBuilder.Entity<ItemAuditLog>(e =>
        {
            e.ToTable("item_audit_log");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Action).IsRequired().HasMaxLength(16);
            e.HasIndex(x => x.CharacterId);
        });
    }
}
