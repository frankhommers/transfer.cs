using System.Formats.Tar;
using System.IO.Compression;
using TransferCs.Api.Services;
using TransferCs.Api.Storage;

namespace TransferCs.Api.Endpoints;

public static class BundleEndpoints
{
    public static WebApplication MapBundleEndpoints(this WebApplication app)
    {
        // Use query-style routes since parentheses in route patterns are problematic in ASP.NET Core
        app.MapGet("/bundle.zip", (HttpRequest request, IStorageProvider storage,
            MetadataService metadataService, CancellationToken ct) =>
            HandleZip(request, storage, metadataService, ct));

        app.MapGet("/bundle.tar", (HttpRequest request, IStorageProvider storage,
            MetadataService metadataService, CancellationToken ct) =>
            HandleTar(request, storage, metadataService, ct));

        app.MapGet("/bundle.tar.gz", (HttpRequest request, IStorageProvider storage,
            MetadataService metadataService, CancellationToken ct) =>
            HandleTarGz(request, storage, metadataService, ct));

        return app;
    }

    private static List<(string Token, string Filename)> ParseFiles(HttpRequest request)
    {
        var filesParam = request.Query["files"].FirstOrDefault() ?? "";
        var result = new List<(string Token, string Filename)>();

        foreach (var entry in filesParam.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var slashIndex = entry.IndexOf('/');
            if (slashIndex > 0 && slashIndex < entry.Length - 1)
            {
                var token = entry[..slashIndex];
                var filename = entry[(slashIndex + 1)..];
                result.Add((token, filename));
            }
        }

        return result;
    }

    private static async Task<IResult> HandleZip(
        HttpRequest request,
        IStorageProvider storage,
        MetadataService metadataService,
        CancellationToken ct)
    {
        var files = ParseFiles(request);
        if (files.Count == 0)
            return Results.BadRequest("No files specified. Use ?files=token1/file1,token2/file2");

        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (token, filename) in files)
            {
                var metadata = await metadataService.CheckAndLoadAsync(token, filename, incrementDownload: true, ct);
                if (metadata == null) continue;

                try
                {
                    var (stream, _) = await storage.GetAsync(token, filename, null, ct);
                    await using (stream)
                    {
                        var entry = archive.CreateEntry(filename, CompressionLevel.Fastest);
                        await using var entryStream = entry.Open();
                        await stream.CopyToAsync(entryStream, ct);
                    }
                }
                catch (Exception ex) when (storage.IsNotExist(ex))
                {
                    // Skip missing files
                }
            }
        }

        ms.Position = 0;
        return Results.File(ms, "application/zip", "bundle.zip");
    }

    private static async Task<IResult> HandleTar(
        HttpRequest request,
        IStorageProvider storage,
        MetadataService metadataService,
        CancellationToken ct)
    {
        var files = ParseFiles(request);
        if (files.Count == 0)
            return Results.BadRequest("No files specified. Use ?files=token1/file1,token2/file2");

        var ms = new MemoryStream();
        await using (var tarWriter = new TarWriter(ms, leaveOpen: true))
        {
            foreach (var (token, filename) in files)
            {
                var metadata = await metadataService.CheckAndLoadAsync(token, filename, incrementDownload: true, ct);
                if (metadata == null) continue;

                try
                {
                    var (stream, contentLength) = await storage.GetAsync(token, filename, null, ct);
                    await using (stream)
                    {
                        // Buffer file content to get exact length for tar header
                        var fileMs = new MemoryStream();
                        await stream.CopyToAsync(fileMs, ct);
                        fileMs.Position = 0;

                        var entry = new PaxTarEntry(TarEntryType.RegularFile, filename)
                        {
                            DataStream = fileMs
                        };
                        await tarWriter.WriteEntryAsync(entry, ct);
                    }
                }
                catch (Exception ex) when (storage.IsNotExist(ex))
                {
                    // Skip missing files
                }
            }
        }

        ms.Position = 0;
        return Results.File(ms, "application/x-tar", "bundle.tar");
    }

    private static async Task<IResult> HandleTarGz(
        HttpRequest request,
        IStorageProvider storage,
        MetadataService metadataService,
        CancellationToken ct)
    {
        var files = ParseFiles(request);
        if (files.Count == 0)
            return Results.BadRequest("No files specified. Use ?files=token1/file1,token2/file2");

        var ms = new MemoryStream();
        await using (var gzStream = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        await using (var tarWriter = new TarWriter(gzStream, leaveOpen: true))
        {
            foreach (var (token, filename) in files)
            {
                var metadata = await metadataService.CheckAndLoadAsync(token, filename, incrementDownload: true, ct);
                if (metadata == null) continue;

                try
                {
                    var (stream, contentLength) = await storage.GetAsync(token, filename, null, ct);
                    await using (stream)
                    {
                        var fileMs = new MemoryStream();
                        await stream.CopyToAsync(fileMs, ct);
                        fileMs.Position = 0;

                        var entry = new PaxTarEntry(TarEntryType.RegularFile, filename)
                        {
                            DataStream = fileMs
                        };
                        await tarWriter.WriteEntryAsync(entry, ct);
                    }
                }
                catch (Exception ex) when (storage.IsNotExist(ex))
                {
                    // Skip missing files
                }
            }
        }

        ms.Position = 0;
        return Results.File(ms, "application/gzip", "bundle.tar.gz");
    }
}
