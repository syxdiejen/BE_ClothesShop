using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SalesApi.Data.Models;
using SalesApi.DTOs;
// DTOs (giữ nguyên ở thư mục DTOs nếu bạn đã tạo sẵn)


[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly SalesAppDbContext _db;
    public ChatController(SalesAppDbContext db) => _db = db;

    // ============== Helper gộp vào controller ==============
    private int GetUserId()
    {
        var s = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("uid");
        if (string.IsNullOrEmpty(s)) throw new UnauthorizedAccessException("Unauthenticated");
        return int.Parse(s);
    }

    // ============== POST: gửi tin nhắn ==============
    // POST /api/chat/send
    [HttpPost("send")]
    public async Task<ActionResult> Send([FromBody] SendMessageDto req, CancellationToken ct)
    {
        var me = GetUserId();
        if (req.ReceiverId <= 0 || req.ReceiverId == me) return BadRequest("receiverId invalid");
        if (string.IsNullOrWhiteSpace(req.Text)) return BadRequest("text required");

        var now = DateTime.UtcNow;

        // Bản ghi cho NGƯỜI GỬI (UserID = me)
        var payloadOut = new ChatPayload { dir = "out", peer = req.ReceiverId, text = req.Text.Trim() };
        _db.ChatMessages.Add(new ChatMessage
        {
            UserID = me,
            Message = JsonSerializer.Serialize(payloadOut),
            SentAt = now
        });

        // Bản ghi cho NGƯỜI NHẬN (UserID = receiver)
        var payloadIn = new ChatPayload { dir = "in", peer = me, text = req.Text.Trim() };
        _db.ChatMessages.Add(new ChatMessage
        {
            UserID = req.ReceiverId,
            Message = JsonSerializer.Serialize(payloadIn),
            SentAt = now
        });

        await _db.SaveChangesAsync(ct);
        return Ok(new { success = true });
    }

    // ============== GET: lấy hội thoại với 1 đối tác ==============
    // GET /api/chat/messages?with=2&page=1&pageSize=50
    [HttpGet("messages")]
    public async Task<ActionResult<List<ChatItemVm>>> GetMessages(
        [FromQuery(Name = "with")] int partnerId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var me = GetUserId();
        if (partnerId <= 0 || partnerId == me) return BadRequest("partner invalid");
        if (page <= 0) page = 1;
        if (pageSize <= 0 || pageSize > 200) pageSize = 50;

        // Do không có cột ReceiverId/Peer trên DB:
        // - Lấy tin nhắn của riêng mình (UserID = me)
        // - Phân trang theo thời gian
        // - Parse JSON và lọc theo peer = partnerId
        var rows = await _db.ChatMessages
            .Where(m => m.UserID == me)
            .OrderBy(m => m.SentAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var list = new List<ChatItemVm>(rows.Count);
        foreach (var r in rows)
        {
            if (string.IsNullOrEmpty(r.Message)) continue;
            ChatPayload? p = null;
            try { p = JsonSerializer.Deserialize<ChatPayload>(r.Message!); } catch { }
            if (p == null || p.peer != partnerId) continue;

            var isOut = string.Equals(p.dir, "out", StringComparison.OrdinalIgnoreCase);
            list.Add(new ChatItemVm
            {
                SenderId = isOut ? me : partnerId,
                ReceiverId = isOut ? partnerId : me,
                Direction = isOut ? "out" : "in",
                Text = p.text,
                SentAt = r.SentAt
            });
        }

        return Ok(list);
    }

    // (tuỳ chọn) GET /api/chat/threads: danh sách đối tác có hội thoại + thời điểm cuối
    // GET /api/chat/threads
    [HttpGet("threads")]
    public async Task<ActionResult> GetThreads(CancellationToken ct)
    {
        var me = GetUserId();

        var rows = await _db.ChatMessages
            .Where(m => m.UserID == me)
            .ToListAsync(ct);

        var dict = new Dictionary<int, DateTime>();
        foreach (var r in rows)
        {
            ChatPayload? p = null;
            try { p = JsonSerializer.Deserialize<ChatPayload>(r.Message!); } catch { }
            if (p == null) continue;
            if (!dict.TryGetValue(p.peer, out var last) || r.SentAt > last)
                dict[p.peer] = r.SentAt;
        }

        var result = dict.OrderByDescending(kv => kv.Value)
                         .Select(kv => new { partnerId = kv.Key, lastMessageAt = kv.Value })
                         .ToList();

        return Ok(result);
    }
}
