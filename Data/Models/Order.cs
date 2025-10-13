using System;
using System.Collections.Generic;

namespace SalesApi.Data.Models;

public partial class Order
{
    public int OrderID { get; set; }

    public int? CartID { get; set; }

    public int? UserID { get; set; }

    public string PaymentMethod { get; set; } = null!;

    public string BillingAddress { get; set; } = null!;

    public string OrderStatus { get; set; } = null!;

    public DateTime OrderDate { get; set; }

    public virtual Cart? Cart { get; set; }

    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();

    public virtual User? User { get; set; }
}
