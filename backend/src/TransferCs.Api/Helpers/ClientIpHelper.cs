using System.Net;

namespace TransferCs.Api.Helpers;

public static class ClientIpHelper
{
  public static string Get(HttpContext context)
  {
    IPAddress? address = context.Connection.RemoteIpAddress;
    if (address == null)
      return "";

    if (address.IsIPv4MappedToIPv6)
      address = address.MapToIPv4();

    return address.ToString();
  }
}
