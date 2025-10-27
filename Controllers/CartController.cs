using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SalesApi.Data.Models;
using SalesApi.DTOs;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class CartController : ControllerBase
{
    private readonly SalesAppDbContext _db;
    public CartController(SalesAppDbContext db) => _db = db;

    //=========== Helpers ===========
    private ActionResult<int> _GetUserIdOr401()
    {
        var s = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("uid");
        if (string.IsNullOrEmpty(s)) return Unauthorized("Bạn cần đăng nhập.");
        if (!int.TryParse(s, out var uid)) return BadRequest("User ID trong token không hợp lệ.");
        return uid;
    }

    private async Task<Cart?> _GetPendingCartWithItemsAsync(int userId, CancellationToken ct, bool includeProduct = false)
    {
        var q = _db.Carts.Where(c => c.UserID == userId && c.Status == "pending").AsQueryable();
        q = includeProduct
            ? q.Include(c => c.CartItems).ThenInclude(i => i.Product)
            : q.Include(c => c.CartItems);
        return await q.FirstOrDefaultAsync(ct);
    }

    private async Task<Cart> _GetOrCreatePendingCartAsync(int userId, CancellationToken ct)
    {
        var cart = await _db.Carts
            .Include(c => c.CartItems)
            .FirstOrDefaultAsync(c => c.UserID == userId && c.Status == "pending", ct);

        if (cart is not null) return cart;

        cart = new Cart
        {
            UserID = userId,
            Status = "pending",
            TotalPrice = 0
        };
        _db.Carts.Add(cart);
        await _db.SaveChangesAsync(ct); // cần CartID trước khi thêm CartItem
        return cart;
    }

    private static void _RecalcTotal(Cart cart)
    {
        cart.TotalPrice = cart.CartItems.Sum(i => i.Price * i.Quantity);
    }

    private static CartReadDto _MapToDto(Cart cart)
    {
        var dto = new CartReadDto
        {
            CartID = cart.CartID,
            Status = cart.CartItems.Count == 0 ? "empty" : cart.Status,
            Items = cart.CartItems.Select(i => new CartItemReadDto
            {
                CartItemID = i.CartItemID,
                ProductID = i.ProductID ?? 0,
                ProductName = i.Product?.ProductName ?? "",
                ImageURL = i.Product?.ImageURL,
                UnitPrice = i.Price,
                Quantity = i.Quantity
            }).ToList()
        };
        return dto;
    }

    private static CartReadDto EmptyDto() => new CartReadDto
    {
        CartID = 0,
        Status = "empty",
        Items = new()
    };

    // ========= Endpoints =========
    // GET /api/cart
    [HttpGet]
    public async Task<ActionResult<CartReadDto>> GetMyCart(CancellationToken ct)
    {
        var uidOrErr = _GetUserIdOr401();
        if (uidOrErr.Result is not null) return uidOrErr.Result!;
        var userId = uidOrErr.Value;

        var cart = await _GetPendingCartWithItemsAsync(userId, ct, includeProduct: true);
        if (cart is null || cart.CartItems.Count == 0) return Ok(EmptyDto());

        return Ok(_MapToDto(cart));
    }

    // POST /api/cart/add-item
    [HttpPost("add-item")]
    public async Task<ActionResult<CartReadDto>> AddItem([FromBody] CartItemCreateDto dto, CancellationToken ct)
    {
        var uidOrErr = _GetUserIdOr401();
        if (uidOrErr.Result is not null) return uidOrErr.Result!;
        var userId = uidOrErr.Value;

        if (dto.Quantity <= 0) return BadRequest("Quantity phải >= 1.");

        var product = await _db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProductID == dto.ProductID, ct);
        if (product is null) return NotFound("Sản phẩm không tồn tại.");

        var cart = await _GetOrCreatePendingCartAsync(userId, ct);

        // tìm nhanh theo CartID + ProductID (không cần Include Product)
        var item = await _db.CartItems
            .FirstOrDefaultAsync(i => i.CartID == cart.CartID && i.ProductID == dto.ProductID, ct);

        if (item is null)
        {
            item = new CartItem
            {
                CartID = cart.CartID,
                ProductID = product.ProductID,
                Quantity = dto.Quantity,
                Price = product.Price // snapshot giá tại thời điểm thêm
            };
            _db.CartItems.Add(item);
        }
        else
        {
            item.Quantity += dto.Quantity;
            item.Price = product.Price; // cập nhật giá hiện tại nếu muốn
        }

        // reload items tối thiểu để tính total & map DTO (không nhất thiết ThenInclude Product)
        await _db.Entry(cart).Collection(c => c.CartItems).LoadAsync(ct);
        _RecalcTotal(cart);
        await _db.SaveChangesAsync(ct);

        // map có ProductName/Image → load Product cho các item một lượt
        await _db.Entry(cart).Collection(c => c.CartItems).Query().Include(i => i.Product).LoadAsync(ct);
        return Ok(_MapToDto(cart));
    }

    // PUT /api/cart/update-item?productId=...
    [HttpPut("update-item")]
    public async Task<ActionResult<CartReadDto>> UpdateItem([FromBody] CartItemUpdateDto dto, [FromQuery] int productId, CancellationToken ct)
    {
        var uidOrErr = _GetUserIdOr401();
        if (uidOrErr.Result is not null) return uidOrErr.Result!;
        var userId = uidOrErr.Value;

        var cart = await _GetPendingCartWithItemsAsync(userId, ct, includeProduct: false);
        if (cart is null) return NotFound("Giỏ hàng không tồn tại.");

        var item = await _db.CartItems
            .FirstOrDefaultAsync(i => i.CartID == cart.CartID && i.ProductID == productId, ct);
        if (item is null) return NotFound("Sản phẩm không có trong giỏ hàng.");

        if (dto.Quantity <= 0)
        {
            _db.CartItems.Remove(item);
        }
        else
        {
            item.Quantity = dto.Quantity;
        }

        await _db.Entry(cart).Collection(c => c.CartItems).LoadAsync(ct);
        if (cart.CartItems.Count == 0)
        {
            _db.Carts.Remove(cart);
            await _db.SaveChangesAsync(ct);
            return Ok(EmptyDto());
        }

        _RecalcTotal(cart);
        await _db.SaveChangesAsync(ct);

        // map có Product info
        await _db.Entry(cart).Collection(c => c.CartItems).Query().Include(i => i.Product).LoadAsync(ct);
        return Ok(_MapToDto(cart));
    }

    // DELETE /api/cart/remove-item/{productId}
    [HttpDelete("remove-item/{productId:int}")]
    public async Task<ActionResult<CartReadDto>> RemoveItem(int productId, CancellationToken ct)
    {
        var uidOrErr = _GetUserIdOr401();
        if (uidOrErr.Result is not null) return uidOrErr.Result!;
        var userId = uidOrErr.Value;

        var cart = await _GetPendingCartWithItemsAsync(userId, ct, includeProduct: false);
        if (cart is null) return NotFound("Giỏ hàng không tồn tại.");

        var item = await _db.CartItems
            .FirstOrDefaultAsync(i => i.CartID == cart.CartID && i.ProductID == productId, ct);
        if (item is null) return NotFound("Sản phẩm không có trong giỏ hàng.");

        _db.CartItems.Remove(item);
        await _db.Entry(cart).Collection(c => c.CartItems).LoadAsync(ct);

        if (cart.CartItems.Count == 0)
        {
            _db.Carts.Remove(cart);
            await _db.SaveChangesAsync(ct);
            return Ok(EmptyDto());
        }

        _RecalcTotal(cart);
        await _db.SaveChangesAsync(ct);

        // trả DTO để FE đồng bộ ngay
        await _db.Entry(cart).Collection(c => c.CartItems).Query().Include(i => i.Product).LoadAsync(ct);
        return Ok(_MapToDto(cart));
    }

    // DELETE /api/cart/clear
    [HttpDelete("clear")]
    public async Task<ActionResult<CartReadDto>> ClearCart(CancellationToken ct)
    {
        var uidOrErr = _GetUserIdOr401();
        if (uidOrErr.Result is not null) return uidOrErr.Result!;
        var userId = uidOrErr.Value;

        var cart = await _GetPendingCartWithItemsAsync(userId, ct);
        if (cart is null) return NotFound("Không có giỏ hàng nào để xoá.");

        _db.Carts.Remove(cart);
        await _db.SaveChangesAsync(ct);

        return Ok(EmptyDto());
    }
}
