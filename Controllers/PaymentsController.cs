using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SalesApi.Config;
using SalesApi.Data.Models;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace SalesApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PaymentsController : ControllerBase
{
    private readonly VnPayOptions _opt;
    private readonly SalesAppDbContext _db;

    public PaymentsController(IOptions<VnPayOptions> opt, SalesAppDbContext db)
    {
        _opt = opt.Value;
        _db = db;
    }

    private static string NowVnString()
    {
        var tzId = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "SE Asia Standard Time"
            : "Asia/Ho_Chi_Minh";
        var tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
        return TimeZoneInfo.ConvertTime(DateTime.UtcNow, tz).ToString("yyyyMMddHHmmss");
    }

    // =============================
    // 1️⃣ TẠO URL THANH TOÁN
    // =============================
    public sealed class CreateVnpayReq
    {
        public int OrderId { get; set; }
        public string? BankCode { get; set; }
    }
    // =============================
    // 1️⃣ TẠO URL THANH TOÁN GIỐNG NESTJS
    // =============================
    [HttpPost("vnpay-create")]
    public async Task<IActionResult> Create([FromBody] CreateVnpayReq req, CancellationToken ct)
    {
        var order = await _db.Orders
            .Include(o => o.Cart!).ThenInclude(c => c.CartItems)
            .FirstOrDefaultAsync(o => o.OrderID == req.OrderId, ct);

        if (order == null) return NotFound("Order not found");
        if (!order.OrderStatus.Equals("pending_payment", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Order must be pending_payment");

        var amountVnd = (long)Math.Round(order.Cart!.CartItems.Sum(i => i.Price * i.Quantity));
        var txnRef = $"{order.OrderID}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var clientIp = "127.0.0.1";

        var vnp = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["vnp_Version"] = _opt.Version,
            ["vnp_Command"] = _opt.Command,
            ["vnp_TmnCode"] = _opt.TmnCode,
            ["vnp_Amount"] = (amountVnd * 100).ToString(),
            ["vnp_CurrCode"] = _opt.CurrCode,
            ["vnp_TxnRef"] = txnRef,
            ["vnp_OrderInfo"] = order.OrderID.ToString(),
            ["vnp_OrderType"] = "billpayment",
            ["vnp_Locale"] = _opt.Locale,
            ["vnp_ReturnUrl"] = _opt.ReturnUrl,
            ["vnp_IpAddr"] = clientIp,
            ["vnp_CreateDate"] = NowVnString(),
        };

        if (!string.IsNullOrEmpty(req.BankCode))
            vnp["vnp_BankCode"] = req.BankCode.Trim().ToUpperInvariant();

        // 🔹 Encode + replace %20 => + (giống NestJS)
        string BuildQueryString(SortedDictionary<string, string> dict)
        {
            return string.Join("&", dict.Select(kv =>
                $"{kv.Key}={Uri.EscapeDataString(kv.Value).Replace("%20", "+")}"
            ));
        }

        var rawData = BuildQueryString(vnp);
        var secureHash = HmacSHA512(_opt.HashSecret, rawData);
        vnp["vnp_SecureHash"] = secureHash;

        var finalUrl = $"{_opt.PaymentUrl}?{BuildQueryString(vnp)}";

        return Ok(new
        {
            paymentUrl = finalUrl,
            orderId = order.OrderID,
            amountVnd
        });
    }

    // =============================
    // 2️⃣ RETURN 
    // =============================
    [HttpGet("vnpay-return")]
    public async Task<IActionResult> Return(CancellationToken ct)
    {
        var q = Request.Query.ToDictionary(k => k.Key, v => v.Value.ToString());

        //1) Validate Signature
        if (!q.TryGetValue("vnp_SecureHash", out var sig))
            return Redirect(_opt.FrontendFailUrl);

        var filtered = q
            .Where(x => x.Key != "vnp_SecureHash" && x.Key != "vnp_SecureHashType")
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Value);

        string rawData = string.Join("&", filtered.Select(kv =>
            $"{kv.Key}={kv.Value.Replace(" ", "+")}"
        ));
        var calc = HmacSHA512(_opt.HashSecret, rawData);
        bool valid = sig.Equals(calc, StringComparison.OrdinalIgnoreCase);

        var rsp = q.GetValueOrDefault("vnp_ResponseCode", "");
        var orderStr = q.GetValueOrDefault("vnp_OrderInfo", "");
        var amountStr = q.GetValueOrDefault("vnp_Amount", "");

        if (!valid || !int.TryParse(orderStr, out var orderId))
            return Redirect(_opt.FrontendFailUrl);

        // 2) Load order & đối chiếu số tiền như IPN
        var order = await _db.Orders
                    .Include(o => o.Cart!).ThenInclude(c => c.CartItems)
                    .FirstOrDefaultAsync(o => o.OrderID == orderId, ct);

        if (order == null)
            return Redirect(_opt.FrontendFailUrl);

        var expected = (long)Math.Round(order.Cart!.CartItems.Sum(i => i.Price * i.Quantity));
        if (!long.TryParse(amountStr, out var got) || got / 100 != expected)
            return Redirect(_opt.FrontendFailUrl);

        // 3) Idempotent: nếu đã paid thì thôi (tránh ghi trùng)
        if (order.OrderStatus.Equals("paid", StringComparison.OrdinalIgnoreCase))
            return Redirect($"{_opt.FrontendSuccessUrl}?orderId={order.OrderID}");

        // 4) Cập nhật DB trong transaction (giống IPN)
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            if (rsp == "00")
            {
                order.OrderStatus = "paid";
                _db.Payments.Add(new Payment
                {
                    OrderID = order.OrderID,
                    Amount = expected,
                    PaymentDate = DateTime.UtcNow,
                    PaymentStatus = "Success"
                });
            }
            else if (rsp == "24")
            {
                order.OrderStatus = "cancelled";
                _db.Payments.Add(new Payment
                {
                    OrderID = order.OrderID,
                    Amount = expected,
                    PaymentDate = DateTime.UtcNow,
                    PaymentStatus = "Fail"
                });
            }
            else
            {
                order.OrderStatus = "failed";
                _db.Payments.Add(new Payment
                {
                    OrderID = order.OrderID,
                    Amount = expected,
                    PaymentDate = DateTime.UtcNow,
                    PaymentStatus = "Fail"
                });
            }
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        } catch
        {
            await tx.RollbackAsync(ct);
        }

        var redirect = (rsp == "00" && valid)
            ? $"{_opt.FrontendSuccessUrl}?orderId={orderStr}"
            : _opt.FrontendFailUrl;

        return Redirect(redirect);
    }

    // =============================
    // 3️⃣ IPN GIỐNG NESTJS
    // =============================
    [HttpGet("vnpay-ipn")]
    public async Task<IActionResult> Ipn(CancellationToken ct)
    {
        var q = Request.Query.ToDictionary(k => k.Key, v => v.Value.ToString());
        if (!q.TryGetValue("vnp_SecureHash", out var sig))
            return Ok(new { RspCode = "97", Message = "Missing signature" });

        var filtered = q
            .Where(x => x.Key != "vnp_SecureHash" && x.Key != "vnp_SecureHashType")
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Value);

        string rawData = string.Join("&", filtered.Select(kv =>
            $"{kv.Key}={kv.Value.Replace(" ", "+")}"
        ));

        var calc = HmacSHA512(_opt.HashSecret, rawData);
        if (!sig.Equals(calc, StringComparison.OrdinalIgnoreCase))
            return Ok(new { RspCode = "97", Message = "Invalid signature" });

        if (!q.TryGetValue("vnp_OrderInfo", out var orderStr) || !int.TryParse(orderStr, out var orderId))
            return Ok(new { RspCode = "99", Message = "Invalid order" });

        var order = await _db.Orders
            .Include(o => o.Cart!).ThenInclude(c => c.CartItems)
            .FirstOrDefaultAsync(o => o.OrderID == orderId, ct);

        if (order == null)
            return Ok(new { RspCode = "01", Message = "Order not found" });

        var expected = (long)Math.Round(order.Cart!.CartItems.Sum(i => i.Price * i.Quantity));
        if (!long.TryParse(q.GetValueOrDefault("vnp_Amount"), out var got) || got / 100 != expected)
            return Ok(new { RspCode = "04", Message = "Invalid amount" });

        var rsp = q.GetValueOrDefault("vnp_ResponseCode", "");
        var success = rsp == "00";

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            order.OrderStatus = success ? "paid" : rsp == "24" ? "cancelled" : "failed";

            _db.Payments.Add(new Payment
            {
                OrderID = order.OrderID,
                Amount = expected,
                PaymentDate = DateTime.UtcNow,
                PaymentStatus = success ? "Success" : "Fail"
            });

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return Ok(new
            {
                RspCode = "00",
                Message = success ? "Thanh toán thành công" : "Thanh toán thất bại",
                RedirectUrl = success
                    ? $"{_opt.FrontendSuccessUrl}?orderId={order.OrderID}"
                    : _opt.FrontendFailUrl
            });
        }
        catch
        {
            await tx.RollbackAsync(ct);
            return Ok(new { RspCode = "99", Message = "Server error" });
        }
    }

    // =============================
    // 4️⃣ HÀM HỖ TRỢ
    // =============================
    public static string HmacSHA512(string key, string input)
    {
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}