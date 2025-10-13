using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using SalesApi.Data.Models;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

// ===== Swagger =====
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "SalesApi", Version = "v1" });
});

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

var app = builder.Build();

// ===== Swagger UI =====
app.UseSwagger();
app.UseSwaggerUI(o =>
{
    o.SwaggerEndpoint("/swagger/v1/swagger.json", "SalesApi v1");
});

app.UseHttpsRedirection();
app.UseCors();

// ❌ KHÔNG còn Authentication / Authorization
app.MapControllers();

// Redirect root -> swagger
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.Run();
