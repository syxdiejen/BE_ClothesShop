using System.ComponentModel.DataAnnotations;

public class RegisterRequest
{
    [Required, MinLength(3)]
    public string Username { get; set; } = default!;

    [Required, MinLength(6)]
    public string Password { get; set; } = default!;

    [EmailAddress]
    public string? Email { get; set; }

    [Phone]
    public string? Phone { get; set; }

    public string? Address { get; set; }
}

public class LoginRequest
{
    [Required]
    public string Username { get; set; } = default!;

    [Required]
    public string Password { get; set; } = default!;
}

public class AuthResponse
{
    public int UserId { get; set; }

    public string Username { get; set; } = default!;

    public string? Email { get; set; }

    public string? Role { get; set; }

}
