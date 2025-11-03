using System.Security.Cryptography;
using System.Text;

namespace SalesApi.Services;

public static class VnPaySigner
{
    // 3.1: Chuỗi THÔ để ký: key=value nối &, sort A→Z; bỏ SecureHash/Type
    public static string BuildRawToSign(IDictionary<string,string> dict)
        => string.Join("&", dict
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .Where(kv => kv.Key != "vnp_SecureHash" && kv.Key != "vnp_SecureHashType")
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value}"));

    // 3.2: Chuỗi ENCODE để gửi: key/value URL-encode, sort A→Z
    public static string BuildEncodedQuery(IDictionary<string,string> dict)
        => string.Join("&", dict
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

    // 3.3: HMAC SHA512
    public static string HmacSHA512(string key, string data)
    {
        using var h = new HMACSHA512(Encoding.UTF8.GetBytes(key));
        var hash = h.ComputeHash(Encoding.UTF8.GetBytes(data));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}
