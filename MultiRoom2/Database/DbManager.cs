namespace MultiRoom2.Database;

public class DbManager(DbContext dbContext)
{
    public T Query<T>(Func<DbContext, T> query)
    {
        lock (dbContext)
        {
            return query(dbContext);
        }
    }

    public void Update(Action<DbContext> query)
    {
        lock (dbContext)
        {
            query(dbContext);
        }
    }

    public void Commit()
    {
        lock (dbContext)
        {
            dbContext.SaveChanges();
        }
    }
}