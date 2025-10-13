using System;
using System.Collections.Generic;

namespace SalesApi.Data.Models;

public partial class Payment
{
    public int PaymentID { get; set; }

    public int? OrderID { get; set; }

    public decimal Amount { get; set; }

    public DateTime PaymentDate { get; set; }

    public string PaymentStatus { get; set; } = null!;

    public virtual Order? Order { get; set; }
}
