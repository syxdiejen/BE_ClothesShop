using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SalesApi.Data.Models;
using SalesApi.DTOs;
using System.Security.Claims;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly SalesAppDbContext _db;
    public OrdersController(SalesAppDbContext db) => _db = db;

    private static class CartStatuses
    {
        public const string Pending = "pending";
        public const string Locked = "locked";
    }

    private static class OrderStatuses
    {
        public const string PendingPayment = "pending_payment";
        public const string Paid = "paid";
        public const string Cancelled = "cancelled";
    }

    // POST /api/orders
    [HttpPost]
    public async Task<ActionResult<OrderReadDto>> Create([FromBody] OrderCreateDto req, CancellationToken ct)
    {
        // 1) Lấy userId từ claims
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("uid");
        if (string.IsNullOrEmpty(userIdStr)) return Unauthorized("Bạn cần đăng nhập.");
        if (!int.TryParse(userIdStr, out var userId)) return BadRequest("User ID trong token không hợp lệ.");

        // 2) Validate input đơn giản (nên thêm DataAnnotations ở DTO để ApiController auto-validate)
        if (string.IsNullOrWhiteSpace(req.PaymentMethod)) req.PaymentMethod = "VNPAY";
        if (string.IsNullOrWhiteSpace(req.BillingAddress)) return BadRequest("BillingAddress là bắt buộc.");

        // 3) Lấy giỏ hàng 'pending' của user + TỐI ƯU: chỉ project trường cần
        var cartData = await _db.Carts
            .Where(c => c.UserID == userId && c.Status == CartStatuses.Pending)
            .Select(c => new
            {
                CartEntity = c, // để update trạng thái
                c.CartID,
                ItemCount = c.CartItems.Count,
                SubTotal = c.CartItems.Sum(i => i.Price * i.Quantity),
                Items = c.CartItems.Select(i => new
                {
                    ProductID = i.ProductID ?? 0,
                    i.Quantity,
                    UnitPrice = i.Price,
                    ProductName = i.Product != null ? i.Product.ProductName : "",
                    ImageURL = i.Product != null ? i.Product.ImageURL : null
                }).ToList()
            })
            .FirstOrDefaultAsync(ct);

        if (cartData is null) return BadRequest("Không tìm thấy giỏ hàng 'pending' của bạn.");
        if (cartData.ItemCount == 0) return BadRequest("Giỏ hàng rỗng, không thể tạo đơn.");

        // 4) Chặn tạo trùng đơn mở trên cùng cart
        var hasOpenOrder = await _db.Orders
            .AsNoTracking()
            .AnyAsync(o => o.CartID == cartData.CartID
                        && o.OrderStatus != OrderStatuses.Paid
                        && o.OrderStatus != OrderStatuses.Cancelled, ct);
        if (hasOpenOrder) return Conflict("Đã tồn tại đơn hàng đang chờ thanh toán cho giỏ này.");

        // 5) Tạo Order + khóa giỏ (1 lần SaveChanges, EF tự dùng transaction)
        var order = new Order
        {
            CartID = cartData.CartID,
            UserID = userId,
            PaymentMethod = req.PaymentMethod.Trim(),
            BillingAddress = req.BillingAddress.Trim(),
            OrderStatus = OrderStatuses.PendingPayment,
            OrderDate = DateTime.UtcNow
        };

        _db.Orders.Add(order);
        cartData.CartEntity.Status = CartStatuses.Locked;

        await _db.SaveChangesAsync(ct);

        // 6) Map DTO từ dữ liệu đã project (không query lại)
        var dto = new OrderReadDto
        {
            OrderID = order.OrderID,
            CartID = cartData.CartID,
            OrderStatus = order.OrderStatus,
            PaymentMethod = order.PaymentMethod,
            BillingAddress = order.BillingAddress,
            OrderDate = order.OrderDate,
            Items = cartData.Items.Select(i => new OrderItemReadDto
            {
                ProductID = i.ProductID,
                ProductName = i.ProductName,
                ImageURL = i.ImageURL,
                UnitPrice = i.UnitPrice,
                Quantity = i.Quantity
            }).ToList()
        };

        // Nếu có GET /api/orders/{id}: đổi CreatedAtAction tới action đó
        return CreatedAtAction(nameof(Create), new { id = dto.OrderID }, dto);
        // return Ok(dto); // dùng tạm nếu bạn chưa có GET-by-id
    }

    // GET /api/orders/my
    [HttpGet("my")]
    public async Task<ActionResult<IEnumerable<OrderReadDto>>> GetMyOrders(CancellationToken ct)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("uid");
        if (string.IsNullOrEmpty(userIdStr)) return Unauthorized();
        int.TryParse(userIdStr, out var userId);

        var orders = await _db.Orders
            .Include(o => o.Cart!)
                .ThenInclude(c => c.CartItems)
            .Where(o => o.UserID == userId)
            .OrderByDescending(o => o.OrderDate)
            .AsNoTracking()
            .ToListAsync(ct);

        var dtos = orders.Select(o => new OrderReadDto
        {
            OrderID = o.OrderID,
            CartID = o.CartID ?? 0,
            OrderStatus = o.OrderStatus,
            PaymentMethod = o.PaymentMethod,
            BillingAddress = o.BillingAddress,
            OrderDate = o.OrderDate,
            Items = o.Cart?.CartItems.Select(i => new OrderItemReadDto
            {
                ProductID = i.ProductID ?? 0,
                ProductName = "",
                ImageURL = null,
                UnitPrice = i.Price,
                Quantity = i.Quantity
            }).ToList() ?? new List<OrderItemReadDto>()
        }).ToList();

        return Ok(dtos);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateOrder(int id, [FromBody] OrderUpdateDto dto, CancellationToken ct)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("uid");
        if (string.IsNullOrEmpty(userIdStr)) return Unauthorized();
        int.TryParse(userIdStr, out var userId);

        var role = User.FindFirstValue(ClaimTypes.Role);
        bool isAdmin = role == "Admin";

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.OrderID == id, ct);
        if (order == null) return NotFound("Không tìm thấy đơn hàng.");

        // chỉ admin hoặc chủ đơn mới sửa
        if (order.UserID != userId && !isAdmin)
            return Forbid();

        // khách chỉ được đổi địa chỉ khi chưa thanh toán
        if (!isAdmin)
        {
            if (order.OrderStatus != "pending_payment")
                return BadRequest("Không thể chỉnh sửa đơn đã thanh toán hoặc đang xử lý.");
            if (!string.IsNullOrWhiteSpace(dto.BillingAddress))
                order.BillingAddress = dto.BillingAddress.Trim();
        }
        else // admin
        {
            if (!string.IsNullOrWhiteSpace(dto.OrderStatus))
                order.OrderStatus = dto.OrderStatus.Trim();

            if (!string.IsNullOrWhiteSpace(dto.BillingAddress))
                order.BillingAddress = dto.BillingAddress.Trim();
        }

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> CancelOrder(int id, CancellationToken ct)
    {
        // 1️⃣ Lấy userID từ token
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("uid");
        if (string.IsNullOrEmpty(userIdStr)) return Unauthorized();
        int.TryParse(userIdStr, out var userId);

        var role = User.FindFirstValue(ClaimTypes.Role);
        bool isAdmin = role == "Admin";

        // 2️⃣ Tìm đơn hàng
        var order = await _db.Orders
            .Include(o => o.Cart)
            .FirstOrDefaultAsync(o => o.OrderID == id, ct);

        if (order == null)
            return NotFound("Không tìm thấy đơn hàng.");

        // 3️⃣ Chỉ admin hoặc chủ đơn được huỷ
        if (order.UserID != userId && !isAdmin)
            return Forbid();

        // 4️⃣ Chặn huỷ nếu đã thanh toán hoặc giao hàng
        if (order.OrderStatus == OrderStatuses.Paid)
            return BadRequest("Đơn hàng đã thanh toán, không thể huỷ.");

        // 5️⃣ Đổi trạng thái đơn hàng thành 'cancelled'
        order.OrderStatus = OrderStatuses.Cancelled;

        // 6️⃣ Nếu có giỏ hàng liên quan -> mở khoá lại để user có thể đặt lại
        if (order.Cart != null && order.Cart.Status == CartStatuses.Locked)
            order.Cart.Status = CartStatuses.Pending;

        await _db.SaveChangesAsync(ct);

        return NoContent(); // HTTP 204 No Content
    }
}
