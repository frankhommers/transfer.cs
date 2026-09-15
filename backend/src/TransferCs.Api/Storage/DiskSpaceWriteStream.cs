namespace TransferCs.Api.Storage;

internal sealed class DiskSpaceWriteStream(FileStream file, DiskSpaceGuard guard) : Stream
{
  private bool _failed;

  public override bool CanRead => file.CanRead;
  public override bool CanSeek => file.CanSeek;
  public override bool CanWrite => file.CanWrite;
  public override long Length => file.Length;
  public override long Position { get => file.Position; set => file.Position = value; }

  public override int Read(byte[] buffer, int offset, int count) => file.Read(buffer, offset, count);

  public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
    file.ReadAsync(buffer, ct);

  public override long Seek(long offset, SeekOrigin origin) => file.Seek(offset, origin);

  public override void Flush() => file.Flush();

  public override Task FlushAsync(CancellationToken ct) => file.FlushAsync(ct);

  public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

  public override void Write(ReadOnlySpan<byte> buffer)
  {
    guard.WriteLock.Wait();
    try
    {
      CheckWrite(buffer.Length);
      file.Write(buffer);
    }
    finally
    {
      guard.WriteLock.Release();
    }
  }

  public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
    WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();

  public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
  {
    await guard.WriteLock.WaitAsync(ct);
    try
    {
      CheckWrite(buffer.Length);
      await file.WriteAsync(buffer, ct);
    }
    finally
    {
      guard.WriteLock.Release();
    }
  }

  public override void SetLength(long value)
  {
    guard.WriteLock.Wait();
    try
    {
      CheckWrite(Math.Max(0, value - file.Length));
      file.SetLength(value);
    }
    finally
    {
      guard.WriteLock.Release();
    }
  }

  private void CheckWrite(long bytes)
  {
    if (_failed)
      throw new InsufficientStorageException(file.Name);
    try
    {
      guard.EnsureAvailable(file.Name, bytes);
    }
    catch (InsufficientStorageException)
    {
      _failed = true;
      throw;
    }
  }

  protected override void Dispose(bool disposing)
  {
    if (disposing)
      file.Dispose();
    base.Dispose(disposing);
  }

  public override async ValueTask DisposeAsync()
  {
    await file.DisposeAsync();
    GC.SuppressFinalize(this);
  }
}
