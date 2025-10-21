using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SalesApi.Data.Models;
using SalesApi.DTOs;

[ApiController]
[Route("api/[controller]")]
public class CategoriesController : ControllerBase
{
    private readonly SalesAppDbContext _db;
    public CategoriesController(SalesAppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CategoryDTO>>> GetAll()
    {
        var items = await _db.Categories.AsNoTracking()
            .Select(c => new CategoryDTO
            {
                CategoryID = c.CategoryID,
                CategoryName = c.CategoryName
            })
            .ToListAsync();

        return Ok(items);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CategoryDTO>> GetById(int id)
    {
        var category = await _db.Categories.AsNoTracking()
            .Where(c => c.CategoryID == id)
            .Select(c => new CategoryDTO
            {
                CategoryID = c.CategoryID,
                CategoryName = c.CategoryName
            })
            .FirstOrDefaultAsync();
        return category is null ? NotFound() : Ok(category);
    }

    [Authorize(Policy = "RequireAdmin")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCategoryDTO req)
    {
        if (string.IsNullOrWhiteSpace(req.CategoryName))
            return BadRequest("CategoryName is required.");

        var entity = new Category
        {
            CategoryName = req.CategoryName.Trim()
        };
        _db.Categories.Add(entity);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = entity.CategoryID },
            new CategoryDTO
            {
                CategoryID = entity.CategoryID,
                CategoryName = entity.CategoryName
            });
    }

    [Authorize(Policy = "RequireAdmin")]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] CategoryDTO req)
    {
        if (id != req.CategoryID) return BadRequest("Id does not match.");

        var category = await _db.Categories.FindAsync(id);
        if (category is null) return NotFound();

        category.CategoryName = string.IsNullOrWhiteSpace(req.CategoryName) ?
            category.CategoryName : req.CategoryName.Trim();
        return NoContent();
    }

    [Authorize(Policy = "RequireAdmin")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var category = await _db.Categories.FindAsync(id);
        if (category is null) return NotFound();

        // Kiểm tra ràng buộc khóa ngoại với Products
        bool hasProducts = await _db.Products.AnyAsync(p => p.CategoryID == id);
        if (hasProducts)
            return BadRequest("Cannot delete category with associated products.");

        _db.Categories.Remove(category);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
