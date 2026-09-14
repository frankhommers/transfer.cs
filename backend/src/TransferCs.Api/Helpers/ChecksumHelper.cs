using System.Buffers;
using System.Security.Cryptography;

namespace TransferCs.Api.Helpers;

public static class ChecksumHelper
{
  private const int BufferSize = 81920;

  /// <summary>
  /// Copies source to destination while computing the SHA-256 digest of the bytes that pass
  /// through. Single pass: the hash rides along on I/O that happens anyway.
  /// Returns the number of bytes copied and the lowercase hex digest.
  /// </summary>
  public static async Task<(long BytesCopied, string Sha256Hex)> CopyAndHashAsync(
    Stream source, Stream destination, CancellationToken ct = default)
  {
    using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
    long total = 0;

    try
    {
      int read;
      while ((read = await source.ReadAsync(buffer.AsMemory(0, BufferSize), ct)) > 0)
      {
        hash.AppendData(buffer, 0, read);
        await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        total += read;
      }
    }
    finally
    {
      ArrayPool<byte>.Shared.Return(buffer);
    }

    return (total, Convert.ToHexStringLower(hash.GetHashAndReset()));
  }

  /// <summary>
  /// Computes the SHA-256 digest of a stream as lowercase hex.
  /// </summary>
  public static async Task<string> ComputeSha256Async(Stream source, CancellationToken ct = default)
  {
    byte[] digest = await SHA256.HashDataAsync(source, ct);
    return Convert.ToHexStringLower(digest);
  }

  /// <summary>
  /// Compares two hex digests case-insensitively.
  /// </summary>
  public static bool Matches(string expectedHex, string actualHex)
  {
    return string.Equals(expectedHex, actualHex, StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>
  /// Read-through wrapper that hashes every byte read from the inner stream.
  /// Used where the payload is streamed straight into storage and there is no temp file
  /// to hash separately. Read <see cref="Sha256Hex" /> after the stream is fully consumed.
  /// </summary>
  public sealed class HashingReadStream : Stream
  {
    private readonly Stream _inner;
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private string? _result;

    public HashingReadStream(Stream inner)
    {
      _inner = inner;
    }

    /// <summary>Lowercase hex digest of everything read so far.</summary>
    public string Sha256Hex => _result ??= Convert.ToHexStringLower(_hash.GetHashAndReset());

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
      get => throw new NotSupportedException();
      set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
      int read = _inner.Read(buffer, offset, count);
      if (read > 0) _hash.AppendData(buffer, offset, read);
      return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
      int read = await _inner.ReadAsync(buffer, ct);
      if (read > 0) _hash.AppendData(buffer.Span[..read]);
      return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
      int read = await _inner.ReadAsync(buffer.AsMemory(offset, count), ct);
      if (read > 0) _hash.AppendData(buffer, offset, read);
      return read;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
      throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
      throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
      throw new NotSupportedException();
    }

    protected override void Dispose(bool disposing)
    {
      if (disposing)
      {
        // Materialize the digest before the hash goes away, so Sha256Hex stays readable
        // after the stream has been disposed.
        _result ??= Convert.ToHexStringLower(_hash.GetHashAndReset());
        _hash.Dispose();
        _inner.Dispose();
      }

      base.Dispose(disposing);
    }
  }
}
