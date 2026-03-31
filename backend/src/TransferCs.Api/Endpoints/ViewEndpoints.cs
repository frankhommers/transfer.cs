using TransferCs.Api.Helpers;

namespace TransferCs.Api.Endpoints;

public static class ViewEndpoints
{
    public static WebApplication MapViewEndpoints(this WebApplication app)
    {
        app.MapGet("/", HandleRoot);
        return app;
    }

    private static IResult HandleRoot(HttpRequest request)
    {
        if (AcceptHelper.AcceptsHtml(request))
        {
            return Results.Redirect("/index.html");
        }

        var usage = """
                    transfer.sh - Easy file sharing from the command line

                    Usage:
                      Upload:    curl --upload-file ./hello.txt https://transfer.sh/hello.txt
                      Download:  curl https://transfer.sh/<token>/hello.txt -o hello.txt
                      Delete:    curl -X DELETE https://transfer.sh/<token>/hello.txt/<deletion-token>

                    Options:
                      Max-Downloads: 1              Maximum number of downloads
                      Max-Days: 1                   Maximum number of days to keep file

                    Examples:
                      curl --upload-file ./hello.txt https://transfer.sh/hello.txt
                      curl -X PUT --upload-file ./hello.txt https://transfer.sh/hello.txt
                      curl -H "Max-Downloads: 1" --upload-file ./hello.txt https://transfer.sh/hello.txt

                    Powered by transfer.sh - https://github.com/dutcoders/transfer.sh
                    """;

        return Results.Text(usage, "text/plain");
    }
}
