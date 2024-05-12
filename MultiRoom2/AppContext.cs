using Microsoft.EntityFrameworkCore;
using MultiRoom2.Entities;

namespace MultiRoom2;

public sealed class AppContext : DbContext
{
    public DbSet<User> Users => Set<User>();
    public AppContext() => Database.EnsureCreated();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite("Data Source=helloapp.db");
    }
}