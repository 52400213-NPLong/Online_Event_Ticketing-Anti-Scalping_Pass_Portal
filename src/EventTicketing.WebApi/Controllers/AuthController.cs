using EventTicketing.Core.Domain.Entities;
using EventTicketing.Core.Interfaces;
using EventTicketing.Infrastructure.Data;
using EventTicketing.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;

namespace EventTicketing.WebApi.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IUserRepository _userRepository;

    public AuthController(ApplicationDbContext db, IUserRepository userRepository)
    {
        _db = db;
        _userRepository = userRepository;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password) || string.IsNullOrWhiteSpace(request.FullName))
        {
            return BadRequest(new { message = "Email, password, and full name are required." });
        }

        var existing = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);
        if (existing is not null)
        {
            return Conflict(new { message = "User already exists." });
        }

        var user = new User
        {
            Email = request.Email,
            FullName = request.FullName,
            PasswordHash = PasswordHasher.Hash(request.Password),
            Role = "CUSTOMER"
        };

        await _userRepository.AddAsync(user, cancellationToken);
        return Ok(new { userId = user.Id, email = user.Email, fullName = user.FullName, role = user.Role });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "Email and password are required." });
        }

        var user = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);
        if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        var token = new JwtTokenGenerator("EventTicketing", "EventTicketingApi", "SuperSecretProductionKey999!").GenerateToken(user.Id.ToString(), user.Role);
        return Ok(new { token, userId = user.Id, email = user.Email, role = user.Role });
    }
}

public record RegisterRequest(string Email, string Password, string FullName);
public record LoginRequest(string Email, string Password);
