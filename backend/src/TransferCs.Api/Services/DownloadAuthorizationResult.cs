namespace TransferCs.Api.Services;

public sealed record DownloadAuthorizationResult(DownloadAuthorizationStatus Status, TimeSpan RetryAfter = default)
{
  public static DownloadAuthorizationResult Allowed { get; } = new(DownloadAuthorizationStatus.Allowed);

  public static DownloadAuthorizationResult MissingCredentials { get; } =
    new(DownloadAuthorizationStatus.MissingCredentials);

  public static DownloadAuthorizationResult WrongPassword { get; } = new(DownloadAuthorizationStatus.WrongPassword);

  public bool IsAllowed => Status == DownloadAuthorizationStatus.Allowed;
}
