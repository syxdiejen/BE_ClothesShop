
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using SalesApi.Config;
using SalesApi.Data.Models;
using Microsoft.Data.SqlClient;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

// ===== Swagger =====
// builder.Services.AddEndpointsApiExplorer();
// builder.Services.AddSwaggerGen(c =>
// {
//     c.SwaggerDoc("v1", new OpenApiInfo { Title = "SalesApi", Version = "v1" });
// });

// ===== Configuration =====
builder.Services.Configure<VnPayOptions>(builder.Configuration.GetSection("VnPay"));
// ===== DbContext =====
builder.Services.AddDbContext<SalesAppDbContext>(options =>
    options.UseSqlServer(cfg.GetConnectionString("DefaultConnection")));

// ===== CORS =====
builder.Services.AddCors(opt =>
{
    opt.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

// ===== Controllers =====
builder.Services.AddControllers();
// ===== JWT Authentication & Authorization =====
// Đọc cấu hình JWT
var JwtIssuer = cfg["Jwt:Issuer"] ?? "MyApi";
var JwtAudience = cfg["Jwt:Audience"] ?? "MyClient";
var JwtKey = cfg["Jwt:Key"] ?? "REPLACE_WITH_LONG_RANDOM_SECRET_>=32CHARS";

builder.Services.AddAuthentication(o =>
{
    o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    o.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = JwtIssuer,
        ValidAudience = JwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey)),
        ClockSkew = TimeSpan.Zero,
        NameClaimType = ClaimTypes.NameIdentifier
    };
    
     o.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = ctx =>
        {
            Console.WriteLine("JWT FAILED: " + ctx.Exception.GetType().Name + " - " + ctx.Exception.Message);
            return Task.CompletedTask;
        },
        OnChallenge = ctx =>
        {
            Console.WriteLine("JWT CHALLENGE: " + ctx.ErrorDescription);
            return Task.CompletedTask;
        },
        OnTokenValidated = ctx =>
        {
            var nameid = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            Console.WriteLine($"JWT OK: nameid={nameid}");
            return Task.CompletedTask;
        }
    };
}); 

builder.Services.AddAuthorization(opts =>
{
    // Thêm các policy nếu cần
    opts.AddPolicy("RequireAdmin", policy => policy.RequireRole("admin", "Admin"));
});

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));

builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
// ========= Swagger =========
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "SalesApi", Version = "v1" });

    var securityScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "Nhập: Bearer {token}",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT"
    };
    c.AddSecurityDefinition("Bearer", securityScheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
                Array.Empty<string>()
            }
        });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using (var scope = app.Services.CreateScope())
    {
        try
        {
            var context = scope.ServiceProvider.GetRequiredService<SalesAppDbContext>();

            Console.WriteLine("Attempting to connect...");
            Console.WriteLine($"Connection String: {builder.Configuration.GetConnectionString("DefaultConnection")}");

            // Thử open connection trực tiếp
            var connection = context.Database.GetDbConnection();
            await connection.OpenAsync();
            Console.WriteLine("Database connection: SUCCESS ✓");
            await connection.CloseAsync();
        }
        catch (SqlException sqlEx)
        {
            Console.WriteLine("=== SQL EXCEPTION ===");
            Console.WriteLine($"Message: {sqlEx.Message}");
            Console.WriteLine($"Error Number: {sqlEx.Number}");
            Console.WriteLine($"State: {sqlEx.State}");
            Console.WriteLine($"Class: {sqlEx.Class}");
            Console.WriteLine($"Server: {sqlEx.Server}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("=== GENERAL EXCEPTION ===");
            Console.WriteLine($"Type: {ex.GetType().Name}");
            Console.WriteLine($"Message: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
            }
        }
    }
}

// ===== Swagger UI =====
app.UseSwagger();
app.UseSwaggerUI(o =>
{
    o.SwaggerEndpoint("/swagger/v1/swagger.json", "SalesApi v1");
});

// app.UseHttpsRedirection();
app.UseCors();

app.UseAuthentication(); // ✅ CÓ Authentication
app.UseAuthorization();

// ❌ KHÔNG còn Authentication / Authorization
app.MapControllers();

// Redirect root -> swagger
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.Run();
