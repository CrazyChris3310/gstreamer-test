using Microsoft.EntityFrameworkCore;
using MultiRoom2.Entities;

namespace MultiRoom2;

public sealed class DbContext : Microsoft.EntityFrameworkCore.DbContext
{
    public DbSet<User> Users => Set<User>();

    public DbSet<UserInfo> UserInfos => Set<UserInfo>();

    public DbSet<Conference> Conferences => Set<Conference>();
    
    public DbContext() => Database.EnsureCreated();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite("Data Source=helloapp.db");
    }
}