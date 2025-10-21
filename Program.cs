
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using SalesApi.Data.Models;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

// ===== Swagger =====
// builder.Services.AddEndpointsApiExplorer();
// builder.Services.AddSwaggerGen(c =>
// {
//     c.SwaggerDoc("v1", new OpenApiInfo { Title = "SalesApi", Version = "v1" });
// });

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
        ClockSkew = TimeSpan.FromSeconds(30)
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

// ===== Swagger UI =====
app.UseSwagger();
app.UseSwaggerUI(o =>
{
    o.SwaggerEndpoint("/swagger/v1/swagger.json", "SalesApi v1");
});

app.UseHttpsRedirection();
app.UseCors();

app.UseAuthentication(); // ✅ CÓ Authentication
app.UseAuthorization();

// ❌ KHÔNG còn Authentication / Authorization
app.MapControllers();

// Redirect root -> swagger
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.Run();
