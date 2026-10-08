using System.Collections;
using System.Text.Json;
using Cove.Core.Auth;
using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Cove.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Cove.Filesizes;

// Cove 1.5.1 does not execute owned list-filter providers for performers/studios.
// This request-only adapter leaves the host responsible for permission checks, native
// filters, ordering, pagination and DTOs. It narrows the root EF set before those run.
public static class FilesizeFilterMiddleware
{
    public static async Task InvokeAsync(HttpContext http, RequestDelegate next)
    {
        var segments = http.Request.Path.Value?.Trim('/').Split('/') ?? [];
        if (!HttpMethods.IsPost(http.Request.Method) || segments.Length != 3 || segments[0] != "api"
            || segments[2] is not ("find" or "aggregate")) { await next(http); return; }
        var kind = segments[1] == "galleries" ? "gallery" : segments[1].TrimEnd('s');
        if (!FilesizeFilter.Kinds.Contains(kind)) { await next(http); return; }
        http.Request.EnableBuffering();
        JsonDocument document;
        try { document = await JsonDocument.ParseAsync(http.Request.Body, cancellationToken: http.RequestAborted); }
        catch (JsonException) { http.Request.Body.Position = 0; await next(http); return; }
        http.Request.Body.Position = 0;
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { await next(http); return; }
            if (root.TryGetProperty("filterExpression", out var expression) && ContainsFilesize(expression)) {
                http.Response.StatusCode = 400;
                await http.Response.WriteAsJsonAsync(new { message = "Apply Filesize as a top-level filter alongside the grouped conditions. Cove 1.5.1 cannot apply it inside a condition group." }); return;
            }
            if (!root.TryGetProperty("objectFilter", out var objectFilter) || objectFilter.ValueKind != JsonValueKind.Object
                || !objectFilter.TryGetProperty(FilesizeFilter.Key, out var criterion)) { await next(http); return; }
            FilesizeFilter filter;
            try { filter = FilesizeFilter.Parse(criterion); }
            catch (ArgumentException ex) { http.Response.StatusCode = 400; await http.Response.WriteAsJsonAsync(new { message = ex.Message }, http.RequestAborted); return; }
            var endpoint = http.GetEndpoint();
            if (endpoint?.RequestDelegate is null) { http.Response.StatusCode = 400; await http.Response.WriteAsJsonAsync(new { message = "Filesize filtering requires a Cove list endpoint." }); return; }
            // Middleware precedes authentication in Cove. Defer work to endpoint execution,
            // preserving its metadata, so the host resolves the authenticated principal first.
            http.SetEndpoint(new Endpoint(async context => {
                var services = context.RequestServices;
                var principal = services.GetRequiredService<ICurrentPrincipalAccessor>();
                var required = kind is "performer" or "studio"
                    ? new[] { "files.read", kind + "s.read", "videos.read", "images.read", "galleries.read", "audios.read", "texts.read" }
                    : new[] { "files.read", kind == "gallery" ? "galleries.read" : kind + "s.read" };
                if (principal.Current is null || required.Any(permission => !principal.Current.Has(permission))) {
                    context.Response.StatusCode = principal.Current is null ? 401 : 403;
                    await context.Response.WriteAsJsonAsync(new { message = "Filesize filtering requires file and media read permissions." }); return;
                }
                var original = services.GetRequiredService<CoveContext>();
                var ids = await filter.Apply(SizeQueries.AllTotals(original, kind)).Select(row => row.Id).ToArrayAsync(context.RequestAborted);
                var options = new DbContextOptionsBuilder<CoveContext>((DbContextOptions<CoveContext>)original.GetService<IDbContextOptions>())
                    .UseModel(original.Model).Options;
                await using var restricted = new FilesizeReadContext(options, principal, kind, ids);
                context.RequestServices = new FilesizeReadServices(services, restricted, kind);
                try { await endpoint.RequestDelegate(context); }
                finally { context.RequestServices = services; }
            }, endpoint.Metadata, endpoint.DisplayName));
            await next(http);
        }
    }
    private static bool ContainsFilesize(JsonElement node) => node.ValueKind switch {
        JsonValueKind.Object => node.EnumerateObject().Any(property => property.Name.Equals(FilesizeFilter.Key, StringComparison.OrdinalIgnoreCase) || ContainsFilesize(property.Value)),
        JsonValueKind.Array => node.EnumerateArray().Any(ContainsFilesize), _ => false
    };
}

public sealed class FilesizeReadContext(DbContextOptions<CoveContext> options, ICurrentPrincipalAccessor principal, string kind, int[] ids)
    : CoveContext(options, principal)
{
    public override DbSet<TEntity> Set<TEntity>()
    {
        var original = base.Set<TEntity>();
        var target = kind switch { "video" => typeof(Video), "performer" => typeof(Performer), "studio" => typeof(Studio),
            "image" => typeof(Image), "gallery" => typeof(Gallery), "audio" => typeof(Audio), "text" => typeof(TextDocument), _ => null };
        return typeof(TEntity) == target ? new FilesizeReadSet<TEntity>(original, original.Where(entity => ids.Contains(EF.Property<int>(entity, "Id")))) : original;
    }
}

sealed class FilesizeReadSet<TEntity>(DbSet<TEntity> original, IQueryable<TEntity> query) : DbSet<TEntity>, IQueryable<TEntity>, IAsyncEnumerable<TEntity>, IInfrastructure<IServiceProvider>
    where TEntity : class
{
    public override IEntityType EntityType => original.EntityType;
    Type IQueryable.ElementType => typeof(TEntity);
    System.Linq.Expressions.Expression IQueryable.Expression => query.Expression;
    IQueryProvider IQueryable.Provider => query.Provider;
    IEnumerator<TEntity> IEnumerable<TEntity>.GetEnumerator() => query.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => query.GetEnumerator();
    public override IAsyncEnumerator<TEntity> GetAsyncEnumerator(CancellationToken cancellationToken = default) => query.AsAsyncEnumerable().GetAsyncEnumerator(cancellationToken);
    IServiceProvider IInfrastructure<IServiceProvider>.Instance => ((IInfrastructure<IServiceProvider>)original).Instance;
}

sealed class FilesizeReadServices(IServiceProvider original, CoveContext context, string kind) : IServiceProvider
{
    private readonly Dictionary<Type, object> repositories = [];
    public object? GetService(Type type)
    {
        if (type == typeof(IServiceProvider)) return this;
        if (type == typeof(CoveContext)) return context;
        var repository = kind switch { "video" => typeof(IVideoRepository), "performer" => typeof(IPerformerRepository), "studio" => typeof(IStudioRepository),
            "image" => typeof(IImageRepository), "gallery" => typeof(IGalleryRepository), _ => null };
        if (type != repository) return original.GetService(type);
        if (!repositories.TryGetValue(type, out var value)) {
            var source = original.GetRequiredService(type);
            value = ActivatorUtilities.CreateInstance(this, source.GetType());
            repositories[type] = value;
        }
        return value;
    }
}
