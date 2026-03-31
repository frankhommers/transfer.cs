using System.Text.RegularExpressions;

namespace TransferCs.Api.Storage;

public class StorageRange
{
    public ulong Start { get; set; }
    public ulong Limit { get; set; }
    public string? ContentRange { get; private set; }

    public string RangeHeader =>
        Limit > 0
            ? $"bytes={Start}-{Start + Limit - 1}"
            : $"bytes={Start}-";

    public ulong AcceptLength(ulong contentLength)
    {
        if (Limit == 0)
            Limit = contentLength - Start;

        if (contentLength < Start)
            return contentLength;

        if (Limit > contentLength - Start)
            return contentLength;

        ContentRange = $"bytes {Start}-{Start + Limit - 1}/{contentLength}";
        return Limit;
    }

    public static StorageRange? Parse(string? rangeHeader)
    {
        if (string.IsNullOrEmpty(rangeHeader))
            return null;

        var match = Regex.Match(rangeHeader, @"^bytes=(\d+)-(\d*)$");
        if (!match.Success) return null;

        var start = ulong.Parse(match.Groups[1].Value);
        ulong limit = 0;

        if (match.Groups[2].Value.Length > 0)
        {
            var finish = ulong.Parse(match.Groups[2].Value);
            if (finish < start) return null;
            limit = finish - start + 1;
        }

        return new StorageRange { Start = start, Limit = limit };
    }
}
