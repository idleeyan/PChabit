using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PChabit.Infrastructure.Data;

public class PChabitDbContextFactory : IDesignTimeDbContextFactory<PChabitDbContext>
{
    public PChabitDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PChabitDbContext>();
        optionsBuilder.UseSqlite("Data Source=pchabit.db");
        
        return new PChabitDbContext(optionsBuilder.Options);
    }
}
