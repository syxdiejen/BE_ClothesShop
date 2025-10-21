using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SalesApi.Data.Models;
using SalesApi.DTOs;
using System.Text.Json; // NEW

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly SalesAppDbContext _db;
    public ProductsController(SalesAppDbContext db) => _db = db;

    // ====== Helpers (NEW) ======
    private static List<string> ParseImageURLs(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new();
        raw = raw.Trim();

        // Ưu tiên JSON: ["a","b"]
        if (raw.StartsWith("["))
        {
            try
            {
                var list = JsonSerializer.Deserialize<List<string>>(raw);
                return list?.Where(s => !string.IsNullOrWhiteSpace(s))
                            .Select(s => s.Trim())
                            .Distinct()
                            .ToList() ?? new();
            }
            catch { /* fall back */ }
        }

        // Mặc định dùng dấu | ngăn cách
        return raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .Distinct()
                  .ToList();
    }

    private static string? PackImageURLs(string? single, List<string>? many)
    {
        // Gom "single" + "many" -> unique
        var list = new List<string>();
        if (!string.IsNullOrWhiteSpace(single)) list.Add(single!.Trim());
        if (many is { Count: > 0 })
            list.AddRange(many.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));

        var uniq = list.Distinct().ToList();
        if (uniq.Count == 0) return null;

        // Thử JSON trước (đẹp, dễ parse)
        var json = JsonSerializer.Serialize(uniq);
        if (json.Length <= 255) return json;

        // Dài quá: dùng '|' để tận dụng tối đa 255 ký tự
        var pipe = string.Join('|', uniq);
        return pipe.Length <= 255 ? pipe : pipe[..255]; // cắt an toàn nếu vẫn vượt
    }
    // ====== End Helpers ======

    // GET /api/products?q=&categoryId=&minPrice=&maxPrice&...
    [HttpGet]
    public async Task<ActionResult<List<ProductListItemDto>>> GetAll([FromQuery] string? keyword)
    {
        var q = from p in _db.Products.AsNoTracking()
                join c in _db.Categories.AsNoTracking() on p.CategoryID equals c.CategoryID into gcat
                from c in gcat.DefaultIfEmpty()
                select new
                {
                    P = p,
                    CategoryName = c != null ? c.CategoryName : null
                };

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToLower();
            q = q.Where(x =>
                EF.Functions.Like(x.P.ProductName, $"%{kw}%") ||
                EF.Functions.Like(x.P.BriefDescription ?? "", $"%{kw}%") ||
                EF.Functions.Like(x.CategoryName ?? "", $"%{kw}%")
            );
        }

        var items = await q
            .OrderBy(x => x.P.ProductID)
            .Select(x => new ProductListItemDto
            {
                ProductID = x.P.ProductID,
                ProductName = x.P.ProductName,
                BriefDescription = x.P.BriefDescription,
                Price = x.P.Price,
                ImageURL = x.P.ImageURL,           // vẫn trả về hình đại diện (hoặc chuỗi pack)
                CategoryID = x.P.CategoryID,
                CategoryName = x.CategoryName
            })
            .ToListAsync();

        return Ok(items);
    }

    // GET /api/products/{id}
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductDetailSlimDto>> GetById(int id, CancellationToken ct)
    {
        if (id <= 0) return BadRequest("Id phải > 0.");

        var query = from p in _db.Products.AsNoTracking()
                    join c in _db.Categories.AsNoTracking() on p.CategoryID equals c.CategoryID into gcat
                    from c in gcat.DefaultIfEmpty()
                    where p.ProductID == id
                    select new
                    {
                        P = p,
                        CategoryName = c != null ? c.CategoryName : null
                    };

        var row = await query.FirstOrDefaultAsync(ct);
        if (row is null) return NotFound();

        var dto = new ProductDetailSlimDto
        {
            ProductID = row.P.ProductID,
            ProductName = row.P.ProductName,
            BriefDescription = row.P.BriefDescription,
            FullDescription = row.P.FullDescription,
            TechnicalSpecifications = row.P.TechnicalSpecifications,
            Price = row.P.Price,
            ImageURL = row.P.ImageURL,              // tương thích cũ
            CategoryID = row.P.CategoryID,
            CategoryName = row.CategoryName,
            ImageURLs = ParseImageURLs(row.P.ImageURL) // NEW: tách mảng gửi FE
        };

        return Ok(dto);
    }

    // POST /api/products
    [Authorize(Policy = "RequireAdmin")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ProductCreateDto req)
    {
        if (string.IsNullOrWhiteSpace(req.ProductName) || req.Price <= 0)
            return BadRequest("ProductName bắt buộc và Price phải > 0.");

        if (req.CategoryID is not null)
        {
            bool existCat = await _db.Categories.AnyAsync(c => c.CategoryID == req.CategoryID);
            if (!existCat) return BadRequest("CategoryID không tồn tại.");
        }

        var entity = new Product
        {
            ProductName = req.ProductName.Trim(),
            BriefDescription = req.BriefDescription,
            FullDescription = req.FullDescription,
            TechnicalSpecifications = req.TechnicalSpecifications,
            Price = req.Price,
            // NEW: đóng gói nhiều ảnh vào 1 cột
            ImageURL = PackImageURLs(req.ImageURL, req.ImageURLs),
            CategoryID = req.CategoryID
        };

        _db.Products.Add(entity);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = entity.ProductID }, entity);
    }

    // PUT /api/products/{id}
    [Authorize(Policy = "RequireAdmin")]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ProductUpdateDto req)
    {
        if (id != req.ProductID) return BadRequest("Id không khớp.");
        if (string.IsNullOrWhiteSpace(req.ProductName) || req.Price <= 0)
            return BadRequest("ProductName bắt buộc và Price phải > 0.");

        if (req.CategoryID is not null)
        {
            bool existCat = await _db.Categories.AnyAsync(c => c.CategoryID == req.CategoryID);
            if (!existCat) return BadRequest("CategoryID không tồn tại.");
        }

        var entity = await _db.Products.FirstOrDefaultAsync(x => x.ProductID == id);
        if (entity is null) return NotFound();

        entity.ProductName = req.ProductName.Trim();
        entity.BriefDescription = req.BriefDescription;
        entity.FullDescription = req.FullDescription;
        entity.TechnicalSpecifications = req.TechnicalSpecifications;
        entity.Price = req.Price;
        entity.CategoryID = req.CategoryID;

        // NEW: đóng gói lại nhiều ảnh
        entity.ImageURL = PackImageURLs(req.ImageURL, req.ImageURLs);

        await _db.SaveChangesAsync();
        return NoContent();
    }

    // DELETE /api/products/{id}
    [Authorize(Policy = "RequireAdmin")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var p = await _db.Products.FindAsync(id);
        if (p is null) return NotFound();

        _db.Products.Remove(p);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
