namespace TransferCs.Api.Services;

public sealed class KeyedLock
{
  private readonly SemaphoreSlim[] _locks = Enumerable.Range(0, 256)
    .Select(_ => new SemaphoreSlim(1, 1))
    .ToArray();

  public SemaphoreSlim Get(string siteId, string token, string filename)
  {
    int index = StringComparer.Ordinal.GetHashCode($"{siteId}/{token}/{filename}") & int.MaxValue;
    return _locks[index % _locks.Length];
  }
}
