using System;
using System.Collections.Generic;

namespace SalesApi.Data.Models;

public partial class StoreLocation
{
    public int LocationID { get; set; }

    public decimal Latitude { get; set; }

    public decimal Longitude { get; set; }

    public string Address { get; set; } = null!;
}
