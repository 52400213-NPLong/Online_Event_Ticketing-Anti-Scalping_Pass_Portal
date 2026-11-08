using EventTicketing.Core.Domain.Entities;
using EventTicketing.Core.Interfaces;
using EventTicketing.Infrastructure.Data;

namespace EventTicketing.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly ApplicationDbContext _db;

    public UserRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default)
        => Task.FromResult(_db.Users.FirstOrDefault(u => u.Id == userId));

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
        => Task.FromResult(_db.Users.FirstOrDefault(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)));

    public Task<User> AddAsync(User user, CancellationToken cancellationToken = default)
    {
        _db.Users.Add(user);
        return Task.FromResult(user);
    }
}
