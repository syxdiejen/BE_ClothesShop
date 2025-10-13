using System;
using System.Collections.Generic;

namespace SalesApi.Data.Models;

public partial class ChatMessage
{
    public int ChatMessageID { get; set; }

    public int? UserID { get; set; }

    public string? Message { get; set; }

    public DateTime SentAt { get; set; }

    public virtual User? User { get; set; }
}
