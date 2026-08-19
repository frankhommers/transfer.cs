using System.Net;

namespace TransferCs.Api.Helpers;

public sealed class IpListMatcher
{
  private readonly List<IPAddress> _addresses = [];
  private readonly List<IPNetwork> _networks = [];

  public IpListMatcher(string list)
  {
    string[] entries = list.Split(',', StringSplitOptions.RemoveEmptyEntries |
                                  StringSplitOptions.TrimEntries);
    if (!string.IsNullOrWhiteSpace(list) && entries.Length == 0)
      throw new ArgumentException("IP list must contain at least one IP address or CIDR range.", nameof(list));

    foreach (string entry in entries)
      if (entry.Contains('/'))
      {
        if (IPNetwork.TryParse(entry, out IPNetwork network))
          _networks.Add(network);
        else
          throw new ArgumentException($"Invalid IP list entry: {entry}", nameof(list));
      }
      else if (IPAddress.TryParse(entry, out IPAddress? address))
      {
        _addresses.Add(Normalize(address));
      }
      else
      {
        throw new ArgumentException($"Invalid IP list entry: {entry}", nameof(list));
      }
  }

  public bool IsEmpty => _addresses.Count == 0 && _networks.Count == 0;

  public bool Matches(string clientIp)
  {
    if (!IPAddress.TryParse(clientIp, out IPAddress? address))
      return false;

    address = Normalize(address);
    return _addresses.Contains(address) || _networks.Any(network => network.Contains(address));
  }

  private static IPAddress Normalize(IPAddress address) =>
    address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
