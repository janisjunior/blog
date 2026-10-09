using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using WheelContentManager.Core;

namespace WheelContentManager.Infrastructure;

public sealed class ContentDb(DbContextOptions<ContentDb> options) : DbContext(options)
{
    public DbSet<Brand> Brands => Set<Brand>(); public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Gallery> Galleries => Set<Gallery>(); public DbSet<GalleryImage> GalleryImages => Set<GalleryImage>();
    public DbSet<WheelSpecification> WheelSpecifications => Set<WheelSpecification>(); public DbSet<SourceReference> SourceReferences => Set<SourceReference>();
    public DbSet<Article> Articles => Set<Article>(); public DbSet<ArticleVersion> ArticleVersions => Set<ArticleVersion>();
    public DbSet<PromptTemplate> PromptTemplates => Set<PromptTemplate>(); public DbSet<PromptVersion> PromptVersions => Set<PromptVersion>();
    public DbSet<GenerationJob> GenerationJobs => Set<GenerationJob>(); public DbSet<AutomationRun> AutomationRuns => Set<AutomationRun>();
    public DbSet<NotificationHistory> NotificationHistory => Set<NotificationHistory>(); public DbSet<ApplicationSetting> ApplicationSettings => Set<ApplicationSetting>(); public DbSet<ErrorLog> ErrorLogs => Set<ErrorLog>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Brand>().HasAlternateKey(x => x.Code); b.Entity<Brand>().HasIndex(x => x.Code).IsUnique();
        b.Entity<Gallery>().HasOne<Brand>().WithMany().HasForeignKey(x => x.Brand).HasPrincipalKey(x => x.Code).OnDelete(DeleteBehavior.Restrict); b.Entity<Gallery>().HasIndex(x => new { x.Brand, x.ExternalId }).IsUnique(); b.Entity<Gallery>().HasIndex(x => x.Url).IsUnique();
        b.Entity<Gallery>().HasOne(x => x.Vehicle).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<Gallery>().HasOne(x => x.Specification).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<Gallery>().HasMany(x => x.Images).WithOne().HasForeignKey(x => x.GalleryId);
        b.Entity<Gallery>().HasMany(x => x.Sources).WithOne().HasForeignKey(x => x.GalleryId);
        b.Entity<GalleryImage>().HasIndex(x => new { x.GalleryId, x.Url }).IsUnique();
        b.Entity<Article>().HasOne(x => x.Gallery).WithMany().HasForeignKey(x => x.GalleryId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Article>().HasIndex(x => new { x.AutomationRunId, x.GalleryId }); b.Entity<Article>().HasIndex(x => x.GalleryId).IsUnique();
        b.Entity<ArticleVersion>().HasIndex(x => new { x.ArticleId, x.Language, x.Revision }).IsUnique();
        b.Entity<PromptTemplate>().HasIndex(x => x.Brand).IsUnique(); b.Entity<PromptVersion>().HasIndex(x => new { x.PromptTemplateId, x.Revision }).IsUnique();
        b.Entity<AutomationRun>().HasIndex(x => x.PeriodKey).IsUnique(); b.Entity<NotificationHistory>().HasIndex(x => x.RunId).IsUnique();
        b.Entity<ApplicationSetting>().HasKey(x => x.Key);
        b.Entity<Article>().Ignore(x => x.Display); b.Entity<Vehicle>().Ignore(x => x.Display); b.Entity<Gallery>().Ignore(x => x.Display).Ignore(x => x.Complete);
        foreach (var type in b.Model.GetEntityTypes()) foreach (var property in type.GetProperties())
        {
            if (property.ClrType == typeof(DateTimeOffset)) property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter());
            if (property.ClrType == typeof(DateTimeOffset?)) property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter());
        }
    }
    public IQueryable<Gallery> FullGalleries => Galleries.AsSplitQuery().Include(x => x.Vehicle).Include(x => x.Specification).Include(x => x.Images).Include(x => x.Sources);
    public IQueryable<Article> FullArticles => Articles.AsSplitQuery().Include(x => x.Gallery).ThenInclude(x => x.Vehicle).Include(x => x.Gallery).ThenInclude(x => x.Specification).Include(x => x.Gallery).ThenInclude(x => x.Images).Include(x => x.Gallery).ThenInclude(x => x.Sources).Include(x => x.Versions);
}
public sealed class DesignFactory : IDesignTimeDbContextFactory<ContentDb>
{
    public ContentDb CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<ContentDb>().UseSqlite("Data Source=design.db").Options);
}
public sealed class AppPaths
{
    public string Root { get; }
    public string Database => Path.Combine(Root, "content.db");
    public string Exports => Path.Combine(Root, "Artykuly");
    public string Images => Path.Combine(Root, "Zdjecia");
    public AppPaths(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WheelContentManager");
        foreach (var p in new[] { Root, Exports, Images, Path.Combine(Root, "Secrets") }) Directory.CreateDirectory(p);
    }
}
