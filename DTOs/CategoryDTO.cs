namespace SalesApi.DTOs
{
    public class CategoryDTO
    {
        public int CategoryID { get; set; }

        public string CategoryName { get; set; } = null!;
    }

    public sealed class CreateCategoryDTO
    {
        public required string CategoryName { get; set; }
    }
}
