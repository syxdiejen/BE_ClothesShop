namespace SalesApi.DTOs
{
    // Dữ liệu FE gửi lên để tạo Order
    public sealed class OrderCreateDto
    {
        // Ví dụ: "VNPAY", "COD", "BANK_TRANSFER"
        public string PaymentMethod { get; set; } = "VNPAY"; 

        // Địa chỉ giao/billing: bạn có thể để luôn địa chỉ nhận hàng
        public string BillingAddress { get; set; } = "";
    }
　
    // Dữ liệu trả về cho FE
    public sealed class OrderItemReadDto
    {
        public int ProductID { get; set; }
        public string ProductName { get; set; } = "";
        public string? ImageURL { get; set; }
        public decimal UnitPrice { get; set; }
        public int Quantity { get; set; }
        public decimal LineTotal => UnitPrice * Quantity;
    }

    public sealed class OrderReadDto
    {
        public int OrderID { get; set; }
        public int CartID { get; set; }
        public string OrderStatus { get; set; } = "pending_payment";
        public string PaymentMethod { get; set; } = "";
        public string BillingAddress { get; set; } = "";
        public DateTime OrderDate { get; set; }

        public List<OrderItemReadDto> Items { get; set; } = new();
        public decimal SubTotal => Items.Sum(x => x.LineTotal);
        public decimal Total => SubTotal; // nếu cần thêm ship/discount thì cộng trừ ở đây
    }

    public sealed class OrderUpdateDto
{
    public string? BillingAddress { get; set; }
    public string? OrderStatus { get; set; } // chỉ admin mới có quyền đổi
}
}