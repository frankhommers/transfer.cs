namespace TransferCs.Api.Services;

public static class MigrationCommand
{
  private const string CommandName = "migrate-legacy-data";

  public static bool IsRequested(IReadOnlyList<string> args) =>
    args.Count == 1 && string.Equals(args[0], CommandName, StringComparison.Ordinal);

  public static void Execute(IServiceProvider services, TextWriter output)
  {
    SiteDataMigration migration = services.GetRequiredService<SiteDataMigration>();
    int count = migration.Run();
    string noun = count == 1 ? "directory" : "directories";
    SiteResolver resolver = services.GetRequiredService<SiteResolver>();
    output.WriteLine($"Migrated {count} legacy token {noun} to site '{resolver.InitialSite.Id}'.");
  }
}
