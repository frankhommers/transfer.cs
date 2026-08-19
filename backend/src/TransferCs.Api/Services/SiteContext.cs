namespace TransferCs.Api.Services;

public sealed class SiteContext
{
  private ResolvedSite? _site;

  public ResolvedSite Site => _site ??
    throw new InvalidOperationException("No site has been resolved for this request.");

  public void Resolve(ResolvedSite site)
  {
    if (_site != null)
      throw new InvalidOperationException("A site has already been resolved for this request.");
    _site = site;
  }
}
