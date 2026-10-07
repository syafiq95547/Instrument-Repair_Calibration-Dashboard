using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace InstrumentHub.Api.Data;
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<InstrumentDbContext>
{
    public InstrumentDbContext CreateDbContext(string[] args)
    {
        var connection=Environment.GetEnvironmentVariable("ConnectionStrings__Instruments") ?? @"Server=(localdb)\mssqllocaldb;Database=InstrumentHub;Trusted_Connection=True;MultipleActiveResultSets=true";
        return new InstrumentDbContext(new DbContextOptionsBuilder<InstrumentDbContext>().UseSqlServer(connection).Options);
    }
}
