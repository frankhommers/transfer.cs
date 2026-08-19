using System.Formats.Tar;
using System.IO.Compression;
using Microsoft.Extensions.Options;
using TransferCs.Api.Configuration;
using TransferCs.Api.Helpers;
using TransferCs.Api.Models;
using TransferCs.Api.Services;
using TransferCs.Api.Storage;

namespace TransferCs.Api.Endpoints;

public static class BundleEndpoints
{
  public static WebApplication MapBundleEndpoints(this WebApplication app)
  {
    app.MapGet("/bundle.zip", (HttpRequest request, IStorageProvider storage,
        MetadataService metadataService, IOptions<TransferCsOptions> optionsAccessor, CancellationToken ct) =>
      HandleZipAsync(request, storage, metadataService, optionsAccessor, ct));

    app.MapGet("/bundle.tar", (HttpRequest request, IStorageProvider storage,
        MetadataService metadataService, IOptions<TransferCsOptions> optionsAccessor, CancellationToken ct) =>
      HandleTarAsync(request, storage, metadataService, optionsAccessor, ct));

    app.MapGet("/bundle.tar.gz", (HttpRequest request, IStorageProvider storage,
        MetadataService metadataService, IOptions<TransferCsOptions> optionsAccessor, CancellationToken ct) =>
      HandleTarGzAsync(request, storage, metadataService, optionsAccessor, ct));

    return app;
  }

  private static List<(string Token, string Filename)> ParseFiles(HttpRequest request)
  {
    string filesParam = request.Query["files"].FirstOrDefault() ?? "";
    List<(string Token, string Filename)> result = [];

    foreach (string entry in filesParam.Split(',',
               StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
      int slashIndex = entry.IndexOf('/');
      if (slashIndex > 0 && slashIndex < entry.Length - 1)
      {
        string token = entry[..slashIndex];
        string filename = entry[(slashIndex + 1)..];
        if (StoragePath.IsSafeSegment(token) && StoragePath.IsSafeSegment(filename))
          result.Add((token, filename));
      }
    }

    return result;
  }

  private static async Task<IResult> HandleZipAsync(
    HttpRequest request,
    IStorageProvider storage,
    MetadataService metadataService,
    IOptions<TransferCsOptions> optionsAccessor,
    CancellationToken ct)
  {
    List<(string Token, string Filename)> files = ParseFiles(request);
    if (files.Count == 0)
      return Results.BadRequest("No files specified. Use ?files=token1/file1,token2/file2");

    string tempPath = Path.Combine(Path.GetTempPath(), $"bundle-{Guid.NewGuid():N}.zip");
    FileStream tempFile = new(tempPath, FileMode.Create, FileAccess.ReadWrite,
      FileShare.None, 81920, FileOptions.DeleteOnClose);

    try
    {
      using (ZipArchive archive = new(tempFile, ZipArchiveMode.Create, true))
      {
        foreach ((string token, string filename) in files)
        {
          FileMetadata? metadata = await metadataService.CheckAndLoadAsync(token, filename, false, ct);
          if (metadata == null) continue;

          try
          {
            (Stream stream, _) = await storage.GetAsync(token, filename, null, ct);
            await using (stream)
            {
              if (!await RecordDownloadAsync(request, token, filename, metadata.Generation,
                    metadataService, optionsAccessor, ct))
                continue;

              ZipArchiveEntry entry = archive.CreateEntry(filename, CompressionLevel.Fastest);
              await using Stream entryStream = entry.Open();
              await stream.CopyToAsync(entryStream, ct);
            }
          }
          catch (Exception ex) when (storage.IsNotExist(ex))
          {
            // Skip missing files
          }
        }
      }

      tempFile.Position = 0;
      return Results.File(tempFile, "application/zip", "bundle.zip");
    }
    catch
    {
      await tempFile.DisposeAsync();
      throw;
    }
  }

  private static async Task<IResult> HandleTarAsync(
    HttpRequest request,
    IStorageProvider storage,
    MetadataService metadataService,
    IOptions<TransferCsOptions> optionsAccessor,
    CancellationToken ct)
  {
    List<(string Token, string Filename)> files = ParseFiles(request);
    if (files.Count == 0)
      return Results.BadRequest("No files specified. Use ?files=token1/file1,token2/file2");

    string tempPath = Path.Combine(Path.GetTempPath(), $"bundle-{Guid.NewGuid():N}.tar");
    FileStream tempFile = new(tempPath, FileMode.Create, FileAccess.ReadWrite,
      FileShare.None, 81920, FileOptions.DeleteOnClose);

    try
    {
      await using (TarWriter tarWriter = new(tempFile, true))
      {
        foreach ((string token, string filename) in files)
        {
          FileMetadata? metadata = await metadataService.CheckAndLoadAsync(token, filename, false, ct);
          if (metadata == null) continue;

          try
          {
            (Stream stream, _) = await storage.GetAsync(token, filename, null, ct);
            await using (stream)
            {
              if (!await RecordDownloadAsync(request, token, filename, metadata.Generation,
                    metadataService, optionsAccessor, ct))
                continue;

              PaxTarEntry entry = new(TarEntryType.RegularFile, filename)
              {
                DataStream = stream
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

      tempFile.Position = 0;
      return Results.File(tempFile, "application/x-tar", "bundle.tar");
    }
    catch
    {
      await tempFile.DisposeAsync();
      throw;
    }
  }

  private static async Task<IResult> HandleTarGzAsync(
    HttpRequest request,
    IStorageProvider storage,
    MetadataService metadataService,
    IOptions<TransferCsOptions> optionsAccessor,
    CancellationToken ct)
  {
    List<(string Token, string Filename)> files = ParseFiles(request);
    if (files.Count == 0)
      return Results.BadRequest("No files specified. Use ?files=token1/file1,token2/file2");

    string tempPath = Path.Combine(Path.GetTempPath(), $"bundle-{Guid.NewGuid():N}.tar.gz");
    FileStream tempFile = new(tempPath, FileMode.Create, FileAccess.ReadWrite,
      FileShare.None, 81920, FileOptions.DeleteOnClose);

    try
    {
      await using (GZipStream gzStream = new(tempFile, CompressionLevel.Fastest, true))
      await using (TarWriter tarWriter = new(gzStream, true))
      {
        foreach ((string token, string filename) in files)
        {
          FileMetadata? metadata = await metadataService.CheckAndLoadAsync(token, filename, false, ct);
          if (metadata == null) continue;

          try
          {
            (Stream stream, _) = await storage.GetAsync(token, filename, null, ct);
            await using (stream)
            {
              if (!await RecordDownloadAsync(request, token, filename, metadata.Generation,
                    metadataService, optionsAccessor, ct))
                continue;

              PaxTarEntry entry = new(TarEntryType.RegularFile, filename)
              {
                DataStream = stream
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

      tempFile.Position = 0;
      return Results.File(tempFile, "application/gzip", "bundle.tar.gz");
    }
    catch
    {
      await tempFile.DisposeAsync();
      throw;
    }
  }

  private static async Task<bool> RecordDownloadAsync(HttpRequest request, string token, string filename,
    string expectedGeneration, MetadataService metadataService, IOptions<TransferCsOptions> optionsAccessor,
    CancellationToken ct)
  {
    TransferCsOptions options = optionsAccessor.Value;
    string? downloadIp = options.DownloadLogEnabled ? ClientIpHelper.Get(request.HttpContext) : null;
    FileMetadata? metadata = await metadataService.CheckAndRecordDownloadAsync(
      token, filename, downloadIp, Math.Max(1, options.DownloadLogMaxEntries), expectedGeneration, ct);
    return metadata != null;
  }
}
