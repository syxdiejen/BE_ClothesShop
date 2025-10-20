using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SalesApi.Data.Models;
using SalesApi.DTOs;
using System.ComponentModel.DataAnnotations;

[ApiController]
[Route("api/[controller]")]
public class CartController : ControllerBase
{
    private readonly SalesAppDbContext _db;
    public CartController(SalesAppDbContext db) => _db = db;

    private async Task<CartReadDto> BuildCartDtoAsync(int cartId, CancellationToken ct = default)
    {
        var items = await _db.CartItems
            .AsNoTracking()
            .Where(ci => ci.CartID == cartId)
            .Join(_db.Products.AsNoTracking(),
                  ci => ci.ProductID,
                  p => p.ProductID,
                  (ci, p) => new CartItemReadDto
                  {
                      CartItemID = ci.CartItemID,
                      ProductID = p.ProductID,
                      ProductName = p.ProductName,
                      ImageURL = p.ImageURL,
                      UnitPrice = ci.Price,
                      Quantity = ci.Quantity
                  })
                  .ToListAsync(ct);
        // Trả về
        return new CartReadDto { CartID = cartId, Items = items };
    }

    // (Tuỳ chọn) Tạo cart chủ động – nếu FE muốn xin sẵn cartId
    [HttpPost]
    public async Task<ActionResult<int>> CreateCart(CancellationToken ct)
    {
        var cart = new Cart();
        _db.Carts.Add(cart);
        await _db.SaveChangesAsync(ct);
        return Ok(cart.CartID);
    }

    //Lấy giỏ hàng
    [HttpGet("{cartId:int}")]
    public async Task<ActionResult<CartReadDto>> GetCart(int cartId, CancellationToken ct)
    {
        bool exists = await _db.Carts.AsNoTracking()
            .AnyAsync(c => c.CartID == cartId, ct);
        if (!exists) return NotFound("Cart not found.");

        var dto = await BuildCartDtoAsync(cartId, ct);
        return Ok(dto);
    }

    [HttpPost("add-or-create")]
    public async Task<ActionResult<CartReadDto>> AddOrCreate([FromBody] CartItemCreateDto req, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        //1 Lấy thông tin product
        var product = await _db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProductID == req.ProductID, ct);
        if (product is null)
            return BadRequest("ProductID không tồn tại.");

        //2 Lấy hoặc tạo giỏ hàng
        int cartId;
        if (req.CartID is int existingId)
        {
            var hasCart = await _db.Carts.AnyAsync(c => c.CartID == existingId, ct);
            if (!hasCart)
            {
                var newCart = new Cart();
                _db.Carts.Add(newCart);
                await _db.SaveChangesAsync(ct);
                cartId = newCart.CartID;
            }
            else cartId = existingId;
        }
        else
        {
            var newCart = new Cart();
            _db.Carts.Add(newCart);
            await _db.SaveChangesAsync(ct);
            cartId = newCart.CartID;
        }

        //3 Kiểm tra sản phẩm đã có trong giỏ chưa
        var item = await _db.CartItems
            .FirstOrDefaultAsync(ci => ci.CartID == cartId && ci.ProductID == req.ProductID, ct);

        if (item is null)
        {
            // Thêm mới
            item = new CartItem
            {
                CartID = cartId,
                ProductID = req.ProductID,
                Quantity = req.Quantity,
                Price = product.Price
            };
            _db.CartItems.Add(item);
        }
        else
        {
            // Cập nhật số lượng
            item.Quantity += req.Quantity;
        }
        await _db.SaveChangesAsync(ct);

        //4 Trả về giỏ hàng sau thao tac
        var dto = await BuildCartDtoAsync(cartId, ct);
        return Ok(dto);
    }

    // Cập nhật số lượng 1 item
    // PUT /api/cart/{cartId}/items/{cartItemId}
    [HttpPut("{cartId:int}/items/{cartItemId:int}")]
    public async Task<IActionResult> UpdateItemQuantity(
        int cartId, int cartItemId, [FromBody] CartItemUpdateDto req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var item = await _db.CartItems
            .FirstOrDefaultAsync(ci => ci.CartItemID == cartItemId && ci.CartID == cartId, ct);
        if (item is null) return NotFound();

        item.Quantity = req.Quantity;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // Xoá 1 item trong giỏ
    // DELETE /api/cart/{cartId}/items/{cartItemId}
    [HttpDelete("{cartId:int}/items/{cartItemId:int}")]
    public async Task<IActionResult> RemoveItem(int cartId, int cartItemId, CancellationToken ct)
    {
        var item = await _db.CartItems
            .FirstOrDefaultAsync(ci => ci.CartItemID == cartItemId && ci.CartID == cartId, ct);
        if (item is null) return NotFound();

        _db.CartItems.Remove(item);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }    
        
          // Xoá toàn bộ giỏ
        // DELETE /api/cart/{cartId}
        [HttpDelete("{cartId:int}")]
        public async Task<IActionResult> ClearCart(int cartId, CancellationToken ct)
        {
            var items = await _db.CartItems.Where(ci => ci.CartID == cartId).ToListAsync(ct);
            if (items.Count == 0) return NoContent();

            _db.CartItems.RemoveRange(items);
            await _db.SaveChangesAsync(ct);
            return NoContent();
        }   
}
