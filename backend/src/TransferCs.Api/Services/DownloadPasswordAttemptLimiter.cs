namespace TransferCs.Api.Services;

public sealed class DownloadPasswordAttemptLimiter(TimeProvider timeProvider)
{
  private static readonly TimeSpan _sweepInterval = TimeSpan.FromMinutes(1);
  private readonly Dictionary<(string Site, string Token, string Filename), Queue<DateTimeOffset>> _attempts = [];
  private readonly Dictionary<(string Site, string Token, string Filename), DateTimeOffset> _expiries = [];
  private readonly Lock _lock = new();
  private DateTimeOffset _nextSweep = DateTimeOffset.MinValue;

  /// <summary>
  /// Records the attempt up front so concurrent guesses cannot all slip past the check before
  /// any failure is counted; a successful verification removes it again through <see cref="Reset"/>.
  /// </summary>
  public bool TryBeginAttempt(string site, string token, string filename, int maxAttempts, TimeSpan window,
    out TimeSpan retryAfter)
  {
    retryAfter = TimeSpan.Zero;
    if (maxAttempts <= 0)
      return true;

    DateTimeOffset now = timeProvider.GetUtcNow();
    (string, string, string) key = (site, token, filename);
    lock (_lock)
    {
      SweepExpired(now);
      if (!_attempts.TryGetValue(key, out Queue<DateTimeOffset>? attempts))
      {
        attempts = new Queue<DateTimeOffset>();
        _attempts[key] = attempts;
      }

      while (attempts.Count > 0 && attempts.Peek() + window <= now)
        attempts.Dequeue();

      if (attempts.Count >= maxAttempts)
      {
        retryAfter = TimeSpan.FromSeconds(Math.Max(1, Math.Ceiling((attempts.Peek() + window - now).TotalSeconds)));
        return false;
      }

      attempts.Enqueue(now);
      _expiries[key] = now + window;
      return true;
    }
  }

  public void Reset(string site, string token, string filename)
  {
    lock (_lock)
    {
      _attempts.Remove((site, token, filename));
      _expiries.Remove((site, token, filename));
    }
  }

  private void SweepExpired(DateTimeOffset now)
  {
    if (now < _nextSweep)
      return;
    _nextSweep = now + _sweepInterval;
    foreach (((string, string, string) key, DateTimeOffset expiry) in _expiries.ToArray())
    {
      if (expiry > now)
        continue;
      _attempts.Remove(key);
      _expiries.Remove(key);
    }
  }
}
