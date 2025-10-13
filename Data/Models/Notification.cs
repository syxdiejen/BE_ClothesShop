using System;
using System.Collections.Generic;

namespace SalesApi.Data.Models;

public partial class Notification
{
    public int NotificationID { get; set; }

    public int? UserID { get; set; }

    public string? Message { get; set; }

    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual User? User { get; set; }
}
