namespace SalesApi.DTOs
{
    public sealed class ProductCreateDto
    {
        public required string ProductName { get; set; }
        public string? BriefDescription { get; set; }
        public string? FullDescription { get; set; }
        public string? TechnicalSpecifications { get; set; }
        public required decimal Price { get; set; }
        public string? ImageURL { get; set; }
        public int? CategoryID { get; set; }
        public List<string>? ImageURLs { get; set; }
    }

    public sealed class ProductUpdateDto
    {
        public int ProductID { get; set; }
        public required string ProductName { get; set; }
        public string? BriefDescription { get; set; }
        public string? FullDescription { get; set; }
        public string? TechnicalSpecifications { get; set; }
        public required decimal Price { get; set; }
        public string? ImageURL { get; set; }
        public int? CategoryID { get; set; }
        public List<string>? ImageURLs { get; set; }
    }

    public sealed class ProductListItemDto
    {
        public int ProductID { get; set; }
        public required string ProductName { get; set; }
        public string? BriefDescription { get; set; }
        public decimal Price { get; set; }
        public string? ImageURL { get; set; }
        public int? CategoryID { get; set; }
        public string? CategoryName { get; set; }
        public int SoldCount { get; set; }
        public List<string>? ImageURLs { get; set; }

    }

    public sealed class ProductDetailSlimDto
{
    public int ProductID { get; set; }
    public required string ProductName { get; set; }
    public string? BriefDescription { get; set; }
    public string? FullDescription { get; set; }
    public string? TechnicalSpecifications { get; set; }
    public required decimal Price { get; set; } // hoặc decimal? nếu DB đang nullable
    public string? ImageURL { get; set; }
    public int? CategoryID { get; set; }
    public string? CategoryName { get; set; }
    public List<string>? ImageURLs { get; set; }

}

    public sealed class PagedResultDto<T>
    {
        public required int Page { get; set; }
        public required int PageSize { get; set; }
        public required int Total { get; set; }
        public required List<T> Items { get; set; }
    }
}