using System.Text;
using TransferCs.Api.Services;

namespace TransferCs.Api.Tests.Services;

public class EncryptionServiceTests
{
    [Fact]
    public async Task EncryptAndDecrypt_RoundTrips()
    {
        var original = "Hello, PGP encryption test!"u8.ToArray();
        using var plaintextStream = new MemoryStream(original);
        const string password = "testpassword123";

        var encrypted = await EncryptionService.EncryptAsync(plaintextStream, password);
        Assert.NotNull(encrypted);

        // Encrypted should be different from original
        var encryptedBytes = new MemoryStream();
        await encrypted.CopyToAsync(encryptedBytes);
        Assert.NotEqual(original, encryptedBytes.ToArray());

        // Decrypt should give back original
        encryptedBytes.Position = 0;
        var decrypted = await EncryptionService.DecryptAsync(encryptedBytes, password);
        using var resultMs = new MemoryStream();
        await decrypted.CopyToAsync(resultMs);
        Assert.Equal(original, resultMs.ToArray());
    }

    [Fact]
    public async Task Encrypt_WithEmptyPassword_ReturnsOriginal()
    {
        var original = "No encryption needed"u8.ToArray();
        var stream = new MemoryStream(original);

        var result = await EncryptionService.EncryptAsync(stream, "");

        Assert.Same(stream, result);
    }

    [Fact]
    public async Task Decrypt_WithEmptyPassword_ReturnsOriginal()
    {
        var original = "No decryption needed"u8.ToArray();
        var stream = new MemoryStream(original);

        var result = await EncryptionService.DecryptAsync(stream, "");

        Assert.Same(stream, result);
    }
}
