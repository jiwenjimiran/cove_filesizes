using Cove.Core.Auth;
using Cove.Data;
using Cove.Plugins;
using Cove.Sdk;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cove.Filesizes;

public sealed record SizeEntry(int Id, long Bytes);
public sealed record SizeRequest(int[] Ids);

public sealed class FilesizesExtension : IExtension, IUIExtension, IApiExtension
{
    public string Id => "io.github.jiwenjimiran.filesizes";
    public string Name => "Filesizes";
    public string Version => "1.0.2";
    public string? Description => "Show total file sizes on performer, studio, and video cards.";
    public string? Author => "jiwenji";
    public string? Url => "https://github.com/jiwenjimiran/cove_filesizes";
    public string? IconUrl => null;
    public string? MinCoveVersion => "1.5.1";
    public IReadOnlyList<string> Categories => ["ui", "library"];
    public void ConfigureServices(IServiceCollection services, ExtensionContext context) { }
    public UIManifest GetUIManifest() => new()
    {
        Slots = [
            new UISlotContribution(Id: $"{Id}:cards", Slot: "app-floating-ui", ExtensionId: Id, ComponentName: "Filesizes"),
            new UISlotContribution(Id: $"{Id}:performer-identity", Slot: "performer-card-footer", ExtensionId: Id, ComponentName: "FilesizeCardIdentity"),
            new UISlotContribution(Id: $"{Id}:studio-identity", Slot: "studio-card-footer", ExtensionId: Id, ComponentName: "FilesizeCardIdentity"),
            new UISlotContribution(Id: $"{Id}:video-identity", Slot: "video-card-content", ExtensionId: Id, ComponentName: "FilesizeCardIdentity")
        ]
    };
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        foreach (var kind in new[] { "performer", "studio", "video" })
        {
            var captured = kind;
            var permissions = kind == "video"
                ? new[] { "files.read", "videos.read" }
                : new[] { "files.read", $"{kind}s.read", "videos.read", "images.read", "galleries.read", "audios.read", "texts.read" };
            endpoints.MapPost($"/api/ext/filesizes/{kind}", async (SizeRequest request, HttpContext http, CancellationToken ct) =>
            {
                if (request.Ids is null || request.Ids.Length > 100 || request.Ids.Any(id => id <= 0))
                    return Results.BadRequest(new { message = "Provide at most 100 positive entity IDs." });
                var db = http.RequestServices.GetRequiredService<CoveContext>();
                var entries = await SizeQueries.LoadAsync(db, captured, request.Ids.Distinct().ToArray(), ct);
                return Results.Ok(entries);
            }).RequireCovePermission(permissions);
        }
    }
}

public static class SizeQueries
{
    public static async Task<List<SizeEntry>> LoadAsync(CoveContext db, string kind, int[] ids, CancellationToken ct = default)
    {
        if (kind == "video") return await VideoTotals(db, ids).ToListAsync(ct);
        var entityIds = kind switch {
            "performer" => db.Performers.Where(p => ids.Contains(p.Id)).Select(p => p.Id),
            "studio" => db.Studios.Where(s => ids.Contains(s.Id)).Select(s => s.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var totals = (await entityIds.ToListAsync(ct)).ToDictionary(id => id, _ => 0L);
        // Filter at attribution first, join matching files, then group once for the whole batch.
        // Correlating five full-file scans to each card can time out on million-file libraries.
        foreach (var query in Parts(db, kind, totals.Keys.ToArray()))
            foreach (var entry in await query.ToListAsync(ct))
                totals[entry.Id] = checked(totals[entry.Id] + entry.Bytes);
        return totals.Select(entry => new SizeEntry(entry.Key, entry.Value)).ToList();
    }

    public static IQueryable<SizeEntry> VideoTotals(CoveContext db, int[] ids) =>
        db.Videos.AsNoTracking().Where(v => ids.Contains(v.Id)).Select(v => new SizeEntry(v.Id,
            db.VideoFiles.Where(f => f.VideoId == (v.ParentVideoId ?? v.Id)).Sum(f => (long?)f.Size) ?? 0));

    public static IReadOnlyList<IQueryable<SizeEntry>> Parts(CoveContext db, string kind, int[] ids)
    {
        if (kind == "video") return [VideoTotals(db, ids)];
        if (kind is not ("performer" or "studio")) throw new ArgumentOutOfRangeException(nameof(kind));
        var performer = kind == "performer";
        var videos = performer
            ? db.Videos.SelectMany(v => v.VideoPerformers.Where(l => ids.Contains(l.PerformerId)),
                (v, l) => new Attribution { Id = l.PerformerId, SourceId = v.ParentVideoId ?? v.Id })
            : db.Videos.Where(v => v.StudioId != null && ids.Contains(v.StudioId.Value))
                .Select(v => new Attribution { Id = v.StudioId!.Value, SourceId = v.ParentVideoId ?? v.Id });
        var images = performer
            ? db.Images.SelectMany(i => i.ImagePerformers.Where(l => ids.Contains(l.PerformerId)),
                (i, l) => new Attribution { Id = l.PerformerId, SourceId = i.Id })
            : db.Images.Where(i => i.StudioId != null && ids.Contains(i.StudioId.Value))
                .Select(i => new Attribution { Id = i.StudioId!.Value, SourceId = i.Id });
        var galleries = performer
            ? db.Galleries.SelectMany(g => g.GalleryPerformers.Where(l => ids.Contains(l.PerformerId)),
                (g, l) => new Attribution { Id = l.PerformerId, SourceId = g.Id })
            : db.Galleries.Where(g => g.StudioId != null && ids.Contains(g.StudioId.Value))
                .Select(g => new Attribution { Id = g.StudioId!.Value, SourceId = g.Id });
        var audios = performer
            ? db.Audios.SelectMany(a => a.AudioPerformers.Where(l => ids.Contains(l.PerformerId)),
                (a, l) => new Attribution { Id = l.PerformerId, SourceId = a.Id })
            : db.Audios.Where(a => a.StudioId != null && ids.Contains(a.StudioId.Value))
                .Select(a => new Attribution { Id = a.StudioId!.Value, SourceId = a.Id });
        var texts = performer
            ? db.TextDocuments.SelectMany(t => t.TextPerformers.Where(l => ids.Contains(l.PerformerId)),
                (t, l) => new Attribution { Id = l.PerformerId, SourceId = t.Id })
            : db.TextDocuments.Where(t => t.StudioId != null && ids.Contains(t.StudioId.Value))
                .Select(t => new Attribution { Id = t.StudioId!.Value, SourceId = t.Id });
        return [
            Sum(videos.Distinct(), db.VideoFiles.Select(f => new SourceFile { SourceId = f.VideoId, Bytes = f.Size })),
            Sum(images, db.ImageFiles.Select(f => new SourceFile { SourceId = f.ImageId, Bytes = f.Size })),
            Sum(galleries, db.GalleryFiles.Select(f => new SourceFile { SourceId = f.GalleryId, Bytes = f.Size })),
            Sum(audios, db.AudioFiles.Select(f => new SourceFile { SourceId = f.AudioId, Bytes = f.Size })),
            Sum(texts, db.TextFiles.Select(f => new SourceFile { SourceId = f.TextDocumentId, Bytes = f.Size }))
        ];
    }
    private static IQueryable<SizeEntry> Sum(IQueryable<Attribution> attributions, IQueryable<SourceFile> files) =>
        attributions.Join(files, a => (int?)a.SourceId, f => f.SourceId, (a, f) => new { a.Id, f.Bytes })
            .GroupBy(row => row.Id).Select(group => new SizeEntry(group.Key, group.Sum(row => row.Bytes)));

    private sealed class Attribution { public int Id { get; set; } public int SourceId { get; set; } }
    private sealed class SourceFile { public int? SourceId { get; set; } public long Bytes { get; set; } }
}
