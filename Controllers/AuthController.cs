using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SalesApi.Data.Models;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly SalesAppDbContext _db;
    private readonly IJwtTokenService _jwt;

    public AuthController(SalesAppDbContext db, IJwtTokenService jwt)
    {
        _db = db;
        _jwt = jwt;
    }

    // ========== SIGN UP (không JWT) ==========
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest req)
    {
        // 1) Check username tồn tại
        var exists = await _db.Users.AsNoTracking()
            .AnyAsync(u => u.Username == req.Username);
        if (exists) return Conflict("Username already exists.");

        // 2) Hash mật khẩu (BCrypt)
        var hash = BCrypt.Net.BCrypt.HashPassword(req.Password);

        // 3) Tạo user
        var user = new User
        {
            Username = req.Username,
            PasswordHash = hash,
            Email = req.Email ?? string.Empty,
            PhoneNumber = req.Phone,
            Address = req.Address,
            Role = "customer"
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        // 4) Trả về thông tin cơ bản (không token)
        return Created(string.Empty, new AuthResponse
        {
            UserId = user.UserID,
            Username = user.Username,
            Email = user.Email,
            Role = user.Role
        });
    }

    // ========== LOGIN ==========
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest req)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == req.Username);
        if (user is null) return Unauthorized("Invalid credentials.");

        var ok = BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash);
        if (!ok) return Unauthorized("Invalid credentials.");

        var token = _jwt.CreateToken(
            user.UserID, user.Username, user.Role, user.Email);

        return Ok(new
        {
            token,
            user = new AuthResponse
            {
                UserId = user.UserID,
                Username = user.Username,
                Email = user.Email,
                Role = user.Role
            }
        });
    }

    // ========== ME (mở, không cần token) ==========
    // Ví dụ gọi: GET /api/auth/me?username=taid
    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<object>> Me()
    {
        var uidStr = User.FindFirstValue("uid")
           ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.Name);

        if (!int.TryParse(uidStr, out var uid))
            return Unauthorized("Invalid token: user id claim is not an integer.");
            
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserID == uid);
        if (user is null) return NotFound();

        return Ok(new
        {
            user.UserID,
            user.Username,
            user.Email,
            user.PhoneNumber,
            user.Address,
            user.Role
        });
    }
}