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
    public string Version => "1.0.0";
    public string? Description => "Show total file sizes on performer, studio, and video cards.";
    public string? Author => "jiwenji";
    public string? Url => "https://github.com/jiwenjimiran/cove_filesizes";
    public string? IconUrl => null;
    public string? MinCoveVersion => "1.5.1";
    public IReadOnlyList<string> Categories => ["ui", "library"];
    public void ConfigureServices(IServiceCollection services, ExtensionContext context) { }
    public UIManifest GetUIManifest() => new()
    {
        Slots = [new UISlotContribution(Id: $"{Id}:cards", Slot: "app-floating-ui", ExtensionId: Id, ComponentName: "Filesizes")]
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
                var entries = await SizeQueries.For(db, captured, request.Ids.Distinct().ToArray()).ToListAsync(ct);
                return Results.Ok(entries);
            }).RequireCovePermission(permissions);
        }
    }
}

public static class SizeQueries
{
    // Database-side sums include every attached file, not the host's MaxFileSize metric.
    // Query filters remain active, and no paths or media metadata leave this endpoint.
    public static IQueryable<SizeEntry> For(CoveContext db, string kind, int[] ids) => kind switch
    {
        "video" => db.Videos.AsNoTracking().Where(v => ids.Contains(v.Id)).Select(v => new SizeEntry(v.Id,
            db.VideoFiles.Where(f => f.VideoId == (v.ParentVideoId ?? v.Id)).Sum(f => (long?)f.Size) ?? 0)),
        "performer" => db.Performers.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => new SizeEntry(p.Id,
            (db.VideoFiles.Where(f => db.Videos.Any(v => (v.Id == f.VideoId || v.ParentVideoId == f.VideoId) && v.VideoPerformers.Any(l => l.PerformerId == p.Id))).Sum(f => (long?)f.Size) ?? 0) +
            (db.ImageFiles.Where(f => f.Image != null && f.Image.ImagePerformers.Any(l => l.PerformerId == p.Id)).Sum(f => (long?)f.Size) ?? 0) +
            (db.GalleryFiles.Where(f => f.Gallery != null && f.Gallery.GalleryPerformers.Any(l => l.PerformerId == p.Id)).Sum(f => (long?)f.Size) ?? 0) +
            (db.AudioFiles.Where(f => f.Audio != null && f.Audio.AudioPerformers.Any(l => l.PerformerId == p.Id)).Sum(f => (long?)f.Size) ?? 0) +
            (db.TextFiles.Where(f => f.TextDocument != null && f.TextDocument.TextPerformers.Any(l => l.PerformerId == p.Id)).Sum(f => (long?)f.Size) ?? 0))),
        "studio" => db.Studios.AsNoTracking().Where(s => ids.Contains(s.Id)).Select(s => new SizeEntry(s.Id,
            (db.VideoFiles.Where(f => db.Videos.Any(v => (v.Id == f.VideoId || v.ParentVideoId == f.VideoId) && v.StudioId == s.Id)).Sum(f => (long?)f.Size) ?? 0) +
            (db.ImageFiles.Where(f => f.Image != null && f.Image.StudioId == s.Id).Sum(f => (long?)f.Size) ?? 0) +
            (db.GalleryFiles.Where(f => f.Gallery != null && f.Gallery.StudioId == s.Id).Sum(f => (long?)f.Size) ?? 0) +
            (db.AudioFiles.Where(f => f.Audio != null && f.Audio.StudioId == s.Id).Sum(f => (long?)f.Size) ?? 0) +
            (db.TextFiles.Where(f => f.TextDocument != null && f.TextDocument.StudioId == s.Id).Sum(f => (long?)f.Size) ?? 0))),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
