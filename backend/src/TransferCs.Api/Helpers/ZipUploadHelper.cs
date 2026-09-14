using System.IO.Compression;

namespace TransferCs.Api.Helpers;

public static class ZipUploadHelper
{
  public static async Task WriteAsync(Stream destination, IReadOnlyList<IFormFile> files, CancellationToken ct)
  {
    using ZipArchive archive = new(destination, ZipArchiveMode.Create, leaveOpen: true);
    HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
    foreach (IFormFile file in files)
    {
      ct.ThrowIfCancellationRequested();
      string filename = SanitizeHelper.SanitizeFilename(file.FileName.Replace('\\', '/'));
      filename = string.Concat(filename.Select(character => "<>:\"|?*".Contains(character) ? '_' : character))
        .TrimEnd(' ', '.');
      if (string.IsNullOrEmpty(filename))
        filename = "file";

      string entryName = filename;
      for (int suffix = 2; !names.Add(entryName); suffix++)
        entryName = $"{Path.GetFileNameWithoutExtension(filename)} ({suffix}){Path.GetExtension(filename)}";

      ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
      await using Stream source = file.OpenReadStream();
      await using Stream target = entry.Open();
      await source.CopyToAsync(target, ct);
    }
  }
}
