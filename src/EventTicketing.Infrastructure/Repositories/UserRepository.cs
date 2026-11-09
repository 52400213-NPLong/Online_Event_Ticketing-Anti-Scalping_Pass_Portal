using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
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
        => _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
        => _db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower(), cancellationToken);

    public async Task<User> AddAsync(User user, CancellationToken cancellationToken = default)
    {
        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);
        return user;
    }
}
