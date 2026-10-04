using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Saakh.Api.Data;

/// <summary>
/// Supplies a context to the EF Core design-time tooling (`dotnet ef migrations`,
/// `dotnet ef database update`) without starting the whole host, reading the
/// connection string from the environment when one is set.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SaakhDbContext>
{
    public SaakhDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
                         ?? "Server=localhost,1433;Database=Saakh;User=sa;Password=Your_strong_Password1;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<SaakhDbContext>()
            .UseSqlServer(connection)
            .Options;

        return new SaakhDbContext(options);
    }
}
