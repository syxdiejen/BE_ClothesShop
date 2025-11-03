using System.Security.Cryptography;
using System.Text;

public static class VnPaySigner
{
    /// RAW để ký: sort theo key Ordinal ASC, KHÔNG encode, bỏ vnp_SecureHash & vnp_SecureHashType
    public static string BuildRawToSign(IDictionary<string, string> dict)
    {
        return string.Join("&",
            dict.Where(kv =>
                    kv.Key.StartsWith("vnp_", StringComparison.Ordinal) &&
                    kv.Key != "vnp_SecureHash" &&
                    kv.Key != "vnp_SecureHashType" &&
                    !string.IsNullOrEmpty(kv.Value))
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key}={kv.Value}")
        );
    }

    /// Query gửi đi: sort theo key Ordinal ASC, URL-encode key & value,
    /// sau cùng MỚI thêm vnp_SecureHash (key không encode)
    public static string BuildEncodedQuery(IDictionary<string, string> dict)
    {
        var pairs = dict
            .Where(kv => !string.IsNullOrEmpty(kv.Value) && kv.Key != "vnp_SecureHash")
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv =>
                $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}")
            .ToList();

        if (dict.TryGetValue("vnp_SecureHash", out var sig) && !string.IsNullOrEmpty(sig))
        {
            pairs.Add($"vnp_SecureHash={sig}");
        }
        return string.Join("&", pairs);
    }

    public static string HmacSHA512(string secret, string data)
    {
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}
