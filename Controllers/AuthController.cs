using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SalesApi.Data.Models;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly SalesAppDbContext _db;

    public AuthController(SalesAppDbContext db)
    {
        _db = db;
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

    // ========== LOGIN (không JWT) ==========
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest req)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == req.Username);
        if (user is null) return Unauthorized("Invalid credentials.");

        var ok = BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash);
        if (!ok) return Unauthorized("Invalid credentials.");

        return Ok(new AuthResponse
        {
            UserId = user.UserID,
            Username = user.Username,
            Email = user.Email,
            Role = user.Role
        });
    }

    // ========== ME (mở, không cần token) ==========
    // Ví dụ gọi: GET /api/auth/me?username=taid
    [HttpGet("me")]
    public async Task<ActionResult<object>> Me([FromQuery] string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return BadRequest("username is required.");

        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == username);
        if (user is null) return NotFound();

        return Ok(new
        {
            user.UserID,
            user.Username,
            user.Email,
            user.PhoneNumber,
            user.Address,
            user.Role,
            Note = "Auth removed: open endpoint using ?username="
        });
    }
}