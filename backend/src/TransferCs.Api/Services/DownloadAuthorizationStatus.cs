namespace TransferCs.Api.Services;

public enum DownloadAuthorizationStatus
{
  Allowed,
  MissingCredentials,
  WrongPassword,
  TooManyAttempts
}
