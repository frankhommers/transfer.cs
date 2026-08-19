using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

using IPNetwork = System.Net.IPNetwork;

namespace TransferCs.Api.Configuration;

public static class ForwardedHeadersSetup
{
  public static void Configure(ForwardedHeadersOptions options, string trustedProxies)
  {
    options.ForwardedHeaders = ForwardedHeaders.None;
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();

    if (string.IsNullOrWhiteSpace(trustedProxies))
      return;

    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                               ForwardedHeaders.XForwardedHost |
                               ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = null;

    if (trustedProxies.Trim() == "*")
    {
      options.KnownIPNetworks.Add(new IPNetwork(IPAddress.Any, 0));
      options.KnownIPNetworks.Add(new IPNetwork(IPAddress.IPv6Any, 0));
      return;
    }

    string[] entries = trustedProxies.Split(',', StringSplitOptions.RemoveEmptyEntries |
                                            StringSplitOptions.TrimEntries);
    if (entries.Length == 0)
      throw new ArgumentException("TrustedProxies must contain at least one IP address or CIDR range.",
        nameof(trustedProxies));

    foreach (string entry in entries)
      if (entry.Contains('/'))
      {
        if (IPNetwork.TryParse(entry, out IPNetwork network))
          options.KnownIPNetworks.Add(network);
        else
          throw new ArgumentException($"Invalid TrustedProxies entry: {entry}", nameof(trustedProxies));
      }
      else if (IPAddress.TryParse(entry, out IPAddress? address))
      {
        options.KnownProxies.Add(address);
      }
      else
      {
        throw new ArgumentException($"Invalid TrustedProxies entry: {entry}", nameof(trustedProxies));
      }
  }
}
