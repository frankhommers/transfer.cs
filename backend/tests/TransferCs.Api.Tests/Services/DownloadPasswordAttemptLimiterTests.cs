using TransferCs.Api.Services;
using TransferCs.Api.Tests.Helpers;

namespace TransferCs.Api.Tests.Services;

public class DownloadPasswordAttemptLimiterTests
{
  private static readonly TimeSpan _window = TimeSpan.FromMinutes(15);
  private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero));
  private readonly DownloadPasswordAttemptLimiter _limiter;

  public DownloadPasswordAttemptLimiterTests()
  {
    _limiter = new DownloadPasswordAttemptLimiter(_time);
  }

  [Fact]
  public void TryBeginAttempt_RefusesOnceMaximumIsReached()
  {
    for (int attempt = 0; attempt < 3; attempt++)
      Assert.True(_limiter.TryBeginAttempt("site", "token", "file", 3, _window, out _));

    Assert.False(_limiter.TryBeginAttempt("site", "token", "file", 3, _window, out TimeSpan retryAfter));
    Assert.Equal(_window, retryAfter);
  }

  [Fact]
  public void TryBeginAttempt_UsesSlidingWindow()
  {
    Assert.True(_limiter.TryBeginAttempt("site", "token", "file", 2, _window, out _));
    _time.Advance(TimeSpan.FromMinutes(10));
    Assert.True(_limiter.TryBeginAttempt("site", "token", "file", 2, _window, out _));
    Assert.False(_limiter.TryBeginAttempt("site", "token", "file", 2, _window, out TimeSpan retryAfter));
    Assert.Equal(TimeSpan.FromMinutes(5), retryAfter);

    _time.Advance(TimeSpan.FromMinutes(5));

    Assert.True(_limiter.TryBeginAttempt("site", "token", "file", 2, _window, out _));
    Assert.False(_limiter.TryBeginAttempt("site", "token", "file", 2, _window, out _));
  }

  [Fact]
  public void Reset_ClearsFailures()
  {
    Assert.True(_limiter.TryBeginAttempt("site", "token", "file", 1, _window, out _));
    Assert.False(_limiter.TryBeginAttempt("site", "token", "file", 1, _window, out _));

    _limiter.Reset("site", "token", "file");

    Assert.True(_limiter.TryBeginAttempt("site", "token", "file", 1, _window, out _));
  }

  [Fact]
  public void Keys_AreIsolatedBySiteTokenAndFilename()
  {
    Assert.True(_limiter.TryBeginAttempt("site", "token", "file", 1, _window, out _));

    Assert.True(_limiter.TryBeginAttempt("other", "token", "file", 1, _window, out _));
    Assert.True(_limiter.TryBeginAttempt("site", "other", "file", 1, _window, out _));
    Assert.True(_limiter.TryBeginAttempt("site", "token", "other", 1, _window, out _));
    Assert.True(_limiter.TryBeginAttempt("site", "tok", "en\nfile", 1, _window, out _));
  }

  [Fact]
  public void ZeroMaximum_DisablesLimiting()
  {
    for (int attempt = 0; attempt < 100; attempt++)
      Assert.True(_limiter.TryBeginAttempt("site", "token", "file", 0, _window, out _));
  }

  [Fact]
  public void RetryAfter_IsAtLeastOneSecond()
  {
    Assert.True(_limiter.TryBeginAttempt("site", "token", "file", 1, _window, out _));
    _time.Advance(_window - TimeSpan.FromMilliseconds(1));

    Assert.False(_limiter.TryBeginAttempt("site", "token", "file", 1, _window, out TimeSpan retryAfter));
    Assert.Equal(TimeSpan.FromSeconds(1), retryAfter);
  }
}
