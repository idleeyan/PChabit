using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Tai.Infrastructure.Data;

public class TaiDbContextFactory : IDesignTimeDbContextFactory<TaiDbContext>
{
    public TaiDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TaiDbContext>();
        optionsBuilder.UseSqlite("Data Source=tai.db");
        
        return new TaiDbContext(optionsBuilder.Options);
    }
}
