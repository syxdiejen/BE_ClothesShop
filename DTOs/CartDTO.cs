using System.ComponentModel.DataAnnotations;

namespace SalesApi.DTOs
{
    /// <summary>
    /// Dữ liệu FE gửi khi thêm sản phẩm vào giỏ hàng.
    /// </summary>
    public sealed class CartItemCreateDto
    {
        public int? CartID { get; set; } // nếu null thì tạo giỏ hàng mới

        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "Product ID không hợp lệ")]
        public required int ProductID { get; set; }
        [Range(1,9999, ErrorMessage = "Quantity phải >= 1")]
        public int Quantity { get; set; } = 1; // mặc định 1 nếu FE không truyền
    }

    /// <summary>
    /// Dữ liệu trả về cho client khi đọc giỏ hàng.
    /// </summary>
    public sealed class CartItemReadDto
    {
        public int CartItemID { get; set; }
        public int ProductID { get; set; }
        public string ProductName { get; set; } = "";
        public string? ImageURL { get; set; }
        public decimal UnitPrice { get; set; }
        public int Quantity { get; set; }

        public decimal TotalPrice => UnitPrice * Quantity;
    }

    public sealed class CartItemUpdateDto
    {
        [Range(0, 9999, ErrorMessage = "Quantity phải >= 0.")]
        public int Quantity { get; set; } = 1;
    }
    
    public class CartReadDto
    {
        public int CartID { get; set; }
        public string Status { get; set; } = "pending";
        public List<CartItemReadDto> Items { get; set; } = new();

        public int ItemCount => Items.Sum(i => i.Quantity);
        public decimal SubTotal => Items.Sum(i => i.TotalPrice);
        public decimal Total => SubTotal; // có thể thêm phí ship, thuế sau này
    }
}
