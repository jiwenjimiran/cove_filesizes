using Cove.Core.Entities;
using Cove.Data;
using Cove.Filesizes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Cove.Core.Auth;
using System.Diagnostics;

if (args.Contains("--live"))
{
    var liveConnection = Environment.GetEnvironmentVariable("COVE_FILESIZES_TEST_CONNECTION")
        ?? throw new Exception("Set COVE_FILESIZES_TEST_CONNECTION to a read-only PostgreSQL connection.");
    var principal = new CurrentPrincipalAccessor(); principal.Set(CovePrincipal.System());
    using var live = new CoveContext(new DbContextOptionsBuilder<CoveContext>().UseNpgsql(liveConnection, pg => pg.UseVector()).Options, principal);
    foreach (var kind in new[] { "performer", "studio", "video" })
    {
        var ids = kind switch {
            "performer" => await live.Performers.OrderBy(p => p.Id).Select(p => p.Id).Take(40).ToArrayAsync(),
            "studio" => await live.Studios.OrderBy(s => s.Id).Select(s => s.Id).Take(40).ToArrayAsync(),
            _ => await live.Videos.OrderBy(v => v.Id).Select(v => v.Id).Take(40).ToArrayAsync()
        };
        var timer = Stopwatch.StartNew();
        var entries = await SizeQueries.LoadAsync(live, kind, ids);
        if (entries.Count != ids.Length || entries.Any(entry => entry.Bytes < 0)) throw new Exception("Invalid live totals.");
        if (timer.ElapsedMilliseconds > 5000) throw new Exception("Live totals exceeded the five-second regression budget.");
        Console.WriteLine($"LIVE {kind}: {entries.Count} totals in {timer.ElapsedMilliseconds} ms");
    }
    return;
}

using var connection = new SqliteConnection("Data Source=:memory:");
connection.Open();
var options = new DbContextOptionsBuilder<CoveContext>().UseSqlite(connection).Options;
using var db = new QueryContext(options);
db.Database.EnsureCreated();
// Seed with a plain EF context so host SaveChanges denormalization is not part of this query test.
using (var seed = new DbContext(new DbContextOptionsBuilder().UseSqlite(connection).UseModel(db.Model).Options))
{
    seed.AddRange(new Performer { Id = 1 }, new Performer { Id = 2 }, new Performer { Id = 3 }, new Performer { Id = 4 }, new Studio { Id = 1 }, new Studio { Id = 2 });
    seed.AddRange(new Video { Id = 1, StudioId = 1 }, new Video { Id = 2, StudioId = 1, ParentVideoId = 1 }, new Video { Id = 3, StudioId = 2 });
    seed.AddRange(new VideoPerformer { VideoId = 1, PerformerId = 1 }, new VideoPerformer { VideoId = 2, PerformerId = 1 }, new VideoPerformer { VideoId = 2, PerformerId = 3 }, new VideoPerformer { VideoId = 3, PerformerId = 2 });
    seed.AddRange(new VideoFile { Id = 1, VideoId = 1, Size = 1000 }, new VideoFile { Id = 2, VideoId = 1, Size = 2000 }, new VideoFile { Id = 3, VideoId = 3, Size = 99000 });
    seed.AddRange(new Image { Id = 1, StudioId = 1 }, new Gallery { Id = 1, StudioId = 1 }, new Audio { Id = 1, StudioId = 1 }, new TextDocument { Id = 1, StudioId = 1 });
    seed.AddRange(new ImagePerformer { ImageId = 1, PerformerId = 1 }, new GalleryPerformer { GalleryId = 1, PerformerId = 1 }, new AudioPerformer { AudioId = 1, PerformerId = 1 }, new TextPerformer { TextDocumentId = 1, PerformerId = 1 });
    seed.AddRange(new ImageFile { Id = 4, ImageId = 1, Size = 4000 }, new GalleryFile { Id = 5, GalleryId = 1, Size = 5000 }, new AudioFile { Id = 6, AudioId = 1, Size = 6000 }, new TextFile { Id = 7, TextDocumentId = 1, Size = 7000 });
    seed.SaveChanges();
}
foreach (var (kind, id, expected) in new[] { ("performer", 1, 25000L), ("performer", 2, 99000L), ("performer", 3, 3000L), ("performer", 4, 0L), ("studio", 1, 25000L), ("video", 1, 3000L), ("video", 2, 3000L) })
{
    var entry = (await SizeQueries.LoadAsync(db, kind, [id])).Single();
    if (entry.Bytes != expected) throw new Exception($"{kind}/{id}: expected {expected}, got {entry.Bytes}");
    Console.WriteLine($"PASS {kind}/{id}: {entry.Bytes} bytes");
}
if ((await SizeQueries.LoadAsync(db, "performer", [999])).Any()) throw new Exception("Missing entity must not fabricate a zero total.");
var batch = await SizeQueries.LoadAsync(db, "performer", [1, 2, 3, 4, 999]);
if (batch.Count != 4 || batch.Single(entry => entry.Id == 1).Bytes != 25000 || batch.Single(entry => entry.Id == 2).Bytes != 99000
    || batch.Single(entry => entry.Id == 3).Bytes != 3000 || batch.Single(entry => entry.Id == 4).Bytes != 0)
    throw new Exception("Batch totals must preserve attribution, clip deduplication, zeros, and missing entity behavior.");
Console.WriteLine("PASS mixed performer batch");
var studioBatch = await SizeQueries.LoadAsync(db, "studio", [1, 2]);
if (studioBatch.Single(entry => entry.Id == 1).Bytes != 25000 || studioBatch.Single(entry => entry.Id == 2).Bytes != 99000)
    throw new Exception("Studio batch totals must remain separate.");
Console.WriteLine("PASS mixed studio batch");
// Check the production provider can translate all three queries without making a connection.
using var postgres = new CoveContext(new DbContextOptionsBuilder<CoveContext>().UseNpgsql("Host=localhost;Database=test;Username=test", options => options.UseVector()).Options);
foreach (var kind in new[] { "performer", "studio", "video" })
{
    foreach (var query in SizeQueries.Parts(postgres, kind, [1, 2])) {
        var sql = query.ToQueryString();
        if (!sql.Contains("sum(", StringComparison.OrdinalIgnoreCase)) throw new Exception("Expected database-side aggregation.");
        if (kind != "video" && !sql.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase)) throw new Exception("Totals must aggregate the batch together.");
    }
    Console.WriteLine($"PASS PostgreSQL translation: {kind}");
}

sealed class QueryContext(DbContextOptions<CoveContext> options) : CoveContext(options)
{
    protected override void OnModelCreating(ModelBuilder model)
    {
        // Minimal schema of the real host entity types, with only columns/relationships used by totals.
        var entities = new (Type Type, string[] Keep)[] {
            (typeof(Performer), ["Id"]), (typeof(Studio), ["Id"]),
            (typeof(Video), ["Id", "StudioId", "ParentVideoId", "Files", "VideoPerformers"]),
            (typeof(Image), ["Id", "StudioId", "Files", "ImagePerformers"]),
            (typeof(Gallery), ["Id", "StudioId", "Files", "GalleryPerformers"]),
            (typeof(Audio), ["Id", "StudioId", "Files", "AudioPerformers"]),
            (typeof(TextDocument), ["Id", "StudioId", "Files", "TextPerformers"]),
            (typeof(VideoFile), ["Id", "Size", "VideoId", "Video"]), (typeof(ImageFile), ["Id", "Size", "ImageId", "Image"]),
            (typeof(GalleryFile), ["Id", "Size", "GalleryId", "Gallery"]), (typeof(AudioFile), ["Id", "Size", "AudioId", "Audio"]),
            (typeof(TextFile), ["Id", "Size", "TextDocumentId", "TextDocument"]),
            (typeof(VideoPerformer), ["VideoId", "PerformerId", "Video"]), (typeof(ImagePerformer), ["ImageId", "PerformerId", "Image"]),
            (typeof(GalleryPerformer), ["GalleryId", "PerformerId", "Gallery"]), (typeof(AudioPerformer), ["AudioId", "PerformerId", "Audio"]),
            (typeof(TextPerformer), ["TextDocumentId", "PerformerId", "TextDocument"])
        };
        foreach (var (type, keep) in entities)
        {
            var entity = model.Entity(type).HasBaseType((Type?)null);
            foreach (var property in type.GetProperties()) if (!keep.Contains(property.Name)) entity.Ignore(property.Name);
            entity.ToTable(type.Name);
            if (keep.Contains("Id")) entity.HasKey("Id");
            else entity.HasKey(keep.Where(name => name.EndsWith("Id")).ToArray());
        }
        foreach (var other in model.Model.GetEntityTypes().Select(entity => entity.ClrType).ToArray())
            if (!entities.Any(entity => entity.Type == other)) model.Ignore(other);
    }
}
