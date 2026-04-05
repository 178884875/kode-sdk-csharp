using System.Text;
using FluentAssertions;
using KodaClaw.ChannelHub.Connectors.WeChat;
using Xunit;

namespace KodaClaw.UnitTests.ChannelHub;

/// <summary>
/// L1 单元测试：HttpWeChatCdnClient 的 AES-ECB 加解密 + key 编码解析。
/// 无 HTTP 调用，全部为纯逻辑测试。
/// </summary>
public sealed class WeChatCdnClientTests
{
    // ── AES-ECB 往返 ─────────────────────────────────────────────────────────

    [Fact]
    public void AesEcbEncryptDecrypt_should_roundtrip_arbitrary_bytes()
    {
        var key = new byte[16];
        Random.Shared.NextBytes(key);
        var plain = Encoding.UTF8.GetBytes("Hello, WeChat CDN! 你好世界");

        var encrypted = HttpWeChatCdnClient.AesEcbEncrypt(plain, key);
        var decrypted = HttpWeChatCdnClient.AesEcbDecrypt(encrypted, key);

        decrypted.Should().Equal(plain);
    }

    [Fact]
    public void AesEcbEncrypt_should_pad_to_block_boundary()
    {
        var key = new byte[16];
        var plain = new byte[10]; // 不是 16 的倍数

        var encrypted = HttpWeChatCdnClient.AesEcbEncrypt(plain, key);

        (encrypted.Length % 16).Should().Be(0, "PKCS7 padding should align to 16-byte blocks");
        encrypted.Length.Should().Be(16);
    }

    [Fact]
    public void AesEcbEncrypt_exact_block_size_should_add_full_padding_block()
    {
        var key = new byte[16];
        var plain = new byte[16]; // 正好一个 block

        var encrypted = HttpWeChatCdnClient.AesEcbEncrypt(plain, key);

        // PKCS7：恰好一个 block 时，追加整块 padding
        encrypted.Length.Should().Be(32);
    }

    // ── ParseAesKey 图片格式（base64 of raw 16 bytes）─────────────────────

    [Fact]
    public void ParseAesKey_image_should_decode_raw_base64()
    {
        var rawKey = new byte[16];
        Random.Shared.NextBytes(rawKey);
        var base64 = Convert.ToBase64String(rawKey);

        var parsed = HttpWeChatCdnClient.ParseAesKey(base64, isImage: true);

        parsed.Should().Equal(rawKey);
    }

    // ── ParseAesKey 文件格式（base64 of hex string of 16 bytes）─────────────

    [Fact]
    public void ParseAesKey_file_should_decode_base64_then_hex()
    {
        var rawKey = new byte[16];
        Random.Shared.NextBytes(rawKey);
        var hexStr = Convert.ToHexString(rawKey).ToLowerInvariant();
        var base64OfHex = Convert.ToBase64String(Encoding.UTF8.GetBytes(hexStr));

        var parsed = HttpWeChatCdnClient.ParseAesKey(base64OfHex, isImage: false);

        parsed.Should().Equal(rawKey);
    }

    // ── 图片 vs 文件 key 编码产生不同结果 ────────────────────────────────────

    [Fact]
    public void ParseAesKey_image_and_file_formats_are_distinct()
    {
        var rawKey = new byte[16];
        Random.Shared.NextBytes(rawKey);

        // 图片 key
        var imageKeyB64 = Convert.ToBase64String(rawKey);

        // 文件 key（double-encoded）
        var hexStr = Convert.ToHexString(rawKey).ToLowerInvariant();
        var fileKeyB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(hexStr));

        var parsedImage = HttpWeChatCdnClient.ParseAesKey(imageKeyB64, isImage: true);
        var parsedFile = HttpWeChatCdnClient.ParseAesKey(fileKeyB64, isImage: false);

        parsedImage.Should().Equal(parsedFile, "both formats encode the same underlying key");
    }

    // ── 使用文件 key 加解密往返 ───────────────────────────────────────────────

    [Fact]
    public void AesEcbEncryptDecrypt_with_file_key_encoding_should_roundtrip()
    {
        var rawKey = new byte[16];
        Random.Shared.NextBytes(rawKey);
        var hexStr = Convert.ToHexString(rawKey).ToLowerInvariant();
        var fileKeyB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(hexStr));

        var plain = Encoding.UTF8.GetBytes("Test file content 123");
        var encrypted = HttpWeChatCdnClient.AesEcbEncrypt(plain, rawKey);

        var parsedKey = HttpWeChatCdnClient.ParseAesKey(fileKeyB64, isImage: false);
        var decrypted = HttpWeChatCdnClient.AesEcbDecrypt(encrypted, parsedKey);

        decrypted.Should().Equal(plain);
    }
}
