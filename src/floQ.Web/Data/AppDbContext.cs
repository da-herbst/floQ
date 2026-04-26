using Microsoft.EntityFrameworkCore;

namespace floQ.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // Entities folgen, sobald Domain-Feature beginnen.
}
