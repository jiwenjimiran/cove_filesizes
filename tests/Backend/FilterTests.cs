using System.Text;
using System.Text.Json;
using Cove.Core.Auth;
using Cove.Core.Interfaces;
using Cove.Data;
using Cove.Data.Repositories;
using Cove.Filesizes;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

static class FilterTests
{
    public static async Task Run(CoveContext db)
    {
        var declarations = FilesizeFilter.Declarations("test");
        if (declarations.Count != 7 || declarations.Any(row => row.FilterKey != FilesizeFilter.Key || row.FilterId != null
            || !row.Modifiers!.SequenceEqual(new[] { "EQUALS" }))) throw new Exception("Filters must use only the custom range editor.");
        foreach (var value in new[] { "-1 GB", "NaN", "10 XB", "0.1 B", "999999999999999999999999999999 TB" })
            ExpectInvalid(() => FilesizeFilter.ParseBytes(value));
        foreach (var (modifier, value, expected) in new[] {
            ("EQUALS", "25 KB", new[] { 1 }), ("NOT_EQUALS", "25 KB", new[] { 2, 3, 4 }),
            ("GREATER_THAN", "25 KB", new[] { 2 }), ("LESS_THAN", "25 KB", new[] { 3, 4 }),
            ("BETWEEN", "3 KB..25 KB", new[] { 1, 3 }), ("NOT_BETWEEN", "3 KB..25 KB", new[] { 2, 4 }),
            ("EQUALS", "3 KB..25 KB", new[] { 1, 3 }), ("NOT_EQUALS", "3 KB..25 KB", new[] { 2, 4 }) })
        {
            var parsed = FilesizeFilter.Parse(JsonSerializer.SerializeToElement(new { modifier, value }));
            var actual = await parsed.Apply(SizeQueries.AllTotals(db, "performer")).Select(row => row.Id).Order().ToArrayAsync();
            if (!actual.SequenceEqual(expected)) throw new Exception($"Wrong {modifier} matches.");
        }
        ExpectInvalid(() => FilesizeFilter.Parse(JsonSerializer.SerializeToElement(new { modifier = "BETWEEN", value = "20 GB..10 GB" })));
        foreach (var (value, expected) in new[] {
            ("..25 KB", new[] { 1, 3, 4 }), ("25 KB..", new[] { 1, 2 }), ("..", new[] { 1, 2, 3, 4 }),
            ("0 MB..0 GB", new[] { 4 }), ("0.003 MB..0.000025 GB", new[] { 1, 3 }) }) {
            var parsed = FilesizeFilter.Parse(JsonSerializer.SerializeToElement(new { modifier = "EQUALS", value }));
            var actual = await parsed.Apply(SizeQueries.AllTotals(db, "performer")).Select(row => row.Id).Order().ToArrayAsync();
            if (!actual.SequenceEqual(expected)) throw new Exception($"Wrong inclusive/open range matches for {value}.");
        }
        var accessor = new CurrentPrincipalAccessor();
        using var provider = new ServiceCollection().AddSingleton(db).AddSingleton<ICurrentPrincipalAccessor>(accessor)
            .AddSingleton<IPerformerRepository>(new PerformerRepository(db)).BuildServiceProvider();
        foreach (var kind in FilesizeFilter.Kinds)
        {
            var expected = await new FilesizeFilter("greaterthan", 5000, 5000).Apply(SizeQueries.AllTotals(db, kind)).Select(row => row.Id).Order().ToArrayAsync();
            var http = Request(provider, kind, new { modifier = "GREATER_THAN", value = "5 KB" });
            var executed = false;
            http.SetEndpoint(new Endpoint(async context => {
                executed = true;
                var scoped = context.RequestServices.GetRequiredService<CoveContext>();
                var actual = await SizeQueries.AllTotals(scoped, kind).Select(row => row.Id).Order().ToArrayAsync();
                if (!actual.SequenceEqual(expected)) throw new Exception($"Middleware did not narrow {kind}.");
                if (kind == "performer" && ReferenceEquals(context.RequestServices.GetRequiredService<IPerformerRepository>(), provider.GetRequiredService<IPerformerRepository>()))
                    throw new Exception("Host controller must receive a repository using the narrowed context.");
                await context.Response.WriteAsJsonAsync(new { count = actual.Length });
            }, new EndpointMetadataCollection(), "test-list"));
            accessor.Set(null);
            await FilesizeFilterMiddleware.InvokeAsync(http, async context => {
                // Simulate Cove's authentication/principal middleware between extension middleware and endpoint execution.
                accessor.Set(CovePrincipal.System());
                await context.GetEndpoint()!.RequestDelegate!(context);
            });
            if (!executed || http.Response.StatusCode != 200 || !ReferenceEquals(http.RequestServices, provider)) throw new Exception("Endpoint execution or request-scope restoration failed.");
        }
        var forbidden = Request(provider, "performer", new { modifier = "GREATER_THAN", value = "5 KB" });
        forbidden.SetEndpoint(new Endpoint(_ => throw new Exception("Unauthorized endpoint ran."), new EndpointMetadataCollection(), "forbidden"));
        await FilesizeFilterMiddleware.InvokeAsync(forbidden, async context => {
            accessor.Set(CovePrincipal.Anonymous()); await context.GetEndpoint()!.RequestDelegate!(context);
        });
        if (forbidden.Response.StatusCode != 403) throw new Exception("Missing permissions must fail closed.");
        var invalid = Request(provider, "video", new { modifier = "EQUALS", value = "wrong" });
        await FilesizeFilterMiddleware.InvokeAsync(invalid, _ => throw new Exception("Invalid filter was passed through."));
        if (invalid.Response.StatusCode != 400) throw new Exception("Invalid value must return a validation error.");
        var grouped = Request(provider, "video", new { modifier = "EQUALS", value = "5 KB" });
        grouped.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            filterExpression = new { criteria = new Dictionary<string, object> { [FilesizeFilter.Key] = new { modifier = "EQUALS", value = "5 KB" } } }
        })));
        await FilesizeFilterMiddleware.InvokeAsync(grouped, _ => throw new Exception("Grouped filter was silently ignored."));
        if (grouped.Response.StatusCode != 400) throw new Exception("Unsupported grouped filters must return an explanation.");
        using (var narrowed = new FilesizeReadContext(new DbContextOptionsBuilder<CoveContext>((DbContextOptions<CoveContext>)db.GetService<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptions>()).UseModel(db.Model).Options,
            accessor, "video", [1, 2])) {
            var ids = await narrowed.Videos.Where(video => video.VideoPerformers.Any(link => link.PerformerId == 3)).Select(video => video.Id).ToArrayAsync();
            if (!ids.SequenceEqual(new[] { 2 })) throw new Exception("Drilldown attribution must combine with filesize restrictions.");
        }
        Console.WriteLine("PASS unit/range comparisons, all seven middleware paths, authentication deferral, permissions and scope cleanup");
    }
    private static DefaultHttpContext Request(IServiceProvider services, string kind, object criterion)
    {
        var http = new DefaultHttpContext(); http.RequestServices = services;
        http.Request.Method = "POST"; http.Request.Path = $"/api/{(kind == "gallery" ? "galleries" : kind + "s")}/find";
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { objectFilter = new Dictionary<string, object> { [FilesizeFilter.Key] = criterion } })));
        http.Response.Body = new MemoryStream(); return http;
    }
    private static void ExpectInvalid(Action action)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new Exception("Invalid filesize was accepted.");
    }
}
