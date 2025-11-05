namespace SalesApi.DTOs
{
public sealed class SendMessageDto
{
    public int ReceiverId { get; set; }
    public string Text { get; set; } = "";
}

public sealed class ChatPayload
{
    public string dir { get; set; } = ""; // "in" | "out"
    public int peer { get; set; }         // id người đối thoại
    public string text { get; set; } = "";
}

public sealed class ChatItemVm
{
    public int SenderId { get; set; }
    public int ReceiverId { get; set; }
    public string Direction { get; set; } = ""; // "in" | "out"
    public string Text { get; set; } = "";
    public DateTime SentAt { get; set; }
}
}
