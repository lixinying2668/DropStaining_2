using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Stainer.Web.Infrastructure.Data;

public sealed class StainerDbContextFactory : IDesignTimeDbContextFactory<StainerDbContext>
{
    public StainerDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(DatabasePathResolver.EnvironmentVariableName)
            ?? DatabasePathResolver.BuildDefaultConnectionString(Directory.GetCurrentDirectory());

        DatabaseInitializer.EnsureDatabaseDirectory(connectionString);

        var options = new DbContextOptionsBuilder<StainerDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new StainerDbContext(options);
    }
}
