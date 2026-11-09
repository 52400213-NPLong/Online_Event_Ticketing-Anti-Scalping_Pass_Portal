using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventTicketing.Core.Domain.Entities;
using EventTicketing.Core.Interfaces;
using EventTicketing.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;

namespace EventTicketing.WebApi.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IUserRepository _userRepository;
    private readonly JwtTokenGenerator _jwtGenerator;

    public AuthController(IUserRepository userRepository, JwtTokenGenerator jwtGenerator)
    {
        _userRepository = userRepository;
        _jwtGenerator = jwtGenerator;
    }

    /// <summary>
    /// Đăng ký tài khoản công khai — Mặc định khóa cứng vai trò CUSTOMER để chống leo thang đặc quyền (Privilege Escalation)
    /// </summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.FullName))
        {
            return BadRequest(new { message = "Vui lòng nhập đầy đủ Họ tên, Email và Mật khẩu." });
        }

        if (request.Password.Length < 8)
        {
            return BadRequest(new { message = "Mật khẩu phải có tối thiểu 8 ký tự (theo bảng tham số bảo mật)." });
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var existing = await _userRepository.GetByEmailAsync(normalizedEmail, cancellationToken);
        if (existing is not null)
        {
            return Conflict(new { message = "Email này đã được đăng ký trong hệ thống." });
        }

        // Bảo mật: Tuyệt đối không nhận Role từ client khi đăng ký công khai; tài khoản nội bộ chỉ do ADMIN cấp
        var user = new User
        {
            Email = normalizedEmail,
            FullName = request.FullName.Trim(),
            PasswordHash = PasswordHasher.Hash(request.Password),
            Role = "CUSTOMER",
            CreatedAt = DateTime.UtcNow
        };

        await _userRepository.AddAsync(user, cancellationToken);
        var token = _jwtGenerator.GenerateToken(user.Id.ToString(), user.Role, user.Email, user.FullName);

        return Ok(new
        {
            token,
            userId = user.Id.ToString(),
            email = user.Email,
            fullName = user.FullName,
            role = user.Role,
            message = "Đăng ký tài khoản thành công!"
        });
    }

    /// <summary>
    /// Đăng nhập xác thực mật khẩu băm & cấp JWT Token chứa thông tin phân quyền Role (FR-02, FR-03)
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "Vui lòng nhập Email và Mật khẩu." });
        }

        var user = await _userRepository.GetByEmailAsync(request.Email.Trim(), cancellationToken);
        if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized(new { message = "Email hoặc mật khẩu không chính xác." });
        }

        var token = _jwtGenerator.GenerateToken(user.Id.ToString(), user.Role, user.Email, user.FullName);
        return Ok(new
        {
            token,
            userId = user.Id.ToString(),
            email = user.Email,
            fullName = user.FullName,
            role = user.Role,
            message = "Đăng nhập thành công!"
        });
    }

    /// <summary>
    /// Xác thực phiên đăng nhập hiện tại qua Header Authorization: Bearer {token}
    /// </summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser(CancellationToken cancellationToken)
    {
        var authHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Unauthorized(new { message = "Chưa đăng nhập." });
        }

        var rawToken = authHeader["Bearer ".Length..].Trim();
        var principal = _jwtGenerator.ValidateToken(rawToken);
        if (principal is null)
        {
            return Unauthorized(new { message = "Phiên đăng nhập đã hết hạn hoặc không hợp lệ." });
        }

        var userIdStr = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        var role = principal.FindFirst(ClaimTypes.Role)?.Value ?? "CUSTOMER";
        var email = principal.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;
        var fullName = principal.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;

        if (Guid.TryParse(userIdStr, out var userGuid))
        {
            var dbUser = await _userRepository.GetByIdAsync(userGuid, cancellationToken);
            if (dbUser is not null)
            {
                email = dbUser.Email;
                fullName = dbUser.FullName;
                role = dbUser.Role;
            }
        }

        return Ok(new
        {
            userId = userIdStr,
            email,
            fullName,
            role
        });
    }
}

public record RegisterRequest(string Email, string Password, string FullName);
public record LoginRequest(string Email, string Password);
