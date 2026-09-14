using TransferCs.Api.Helpers;

namespace TransferCs.Api.Models;

public sealed class UploadResult(IReadOnlyList<UploadedFile> files) : IResult
{
  public async Task ExecuteAsync(HttpContext httpContext)
  {
    HttpResponse response = httpContext.Response;
    response.Headers.CacheControl = "no-store";
    response.Headers.Vary = "Accept";
    if (files.Count == 1)
    {
      UploadedFile file = files[0];
      response.Headers.Location = file.Url;
      response.Headers.Append("Link", UploadLinkHelper.Format(file.AdminUrl, UploadLinkHelper.AdminRelation));
      response.Headers.Append("Link", UploadLinkHelper.Format(file.DeleteUrl, UploadLinkHelper.DeleteRelation));
    }

    IResult result = AcceptHelper.PrefersJson(httpContext.Request)
      ? Results.Json(new UploadResponse(files), AppJsonContext.Default.UploadResponse,
        statusCode: StatusCodes.Status201Created)
      : Results.Text(string.Join('\n', files.Select(file => file.Url)) + "\n", "text/plain",
        statusCode: StatusCodes.Status201Created);
    await result.ExecuteAsync(httpContext);
  }
}
