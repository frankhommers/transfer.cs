using System.Text;
using Microsoft.Extensions.Options;
using TransferCs.Api.Configuration;
using TransferCs.Api.Services;
using TransferCs.Api.Storage;

namespace TransferCs.Api.Tests.Services;

public class EncryptionServiceTests
{
  private readonly EncryptionService _encryption = new(new DiskSpaceGuard(
    Options.Create(new TransferCsOptions()), new DiskSpaceProbe()));
  [Fact]
  public async Task EncryptAndDecrypt_RoundTrips()
  {
    byte[] original = "Hello, PGP encryption test!"u8.ToArray();
    using MemoryStream plaintextStream = new(original);
    const string password = "testpassword123";

    Stream encrypted = await _encryption.EncryptAsync(plaintextStream, password);
    Assert.NotNull(encrypted);

    // Encrypted should be different from original
    MemoryStream encryptedBytes = new();
    await encrypted.CopyToAsync(encryptedBytes);
    Assert.NotEqual(original, encryptedBytes.ToArray());

    // Decrypt should give back original
    encryptedBytes.Position = 0;
    Stream decrypted = await _encryption.DecryptAsync(encryptedBytes, password);
    using MemoryStream resultMs = new();
    await decrypted.CopyToAsync(resultMs);
    Assert.Equal(original, resultMs.ToArray());
  }

  [Fact]
  public async Task Encrypt_WithEmptyPassword_ReturnsOriginal()
  {
    byte[] original = "No encryption needed"u8.ToArray();
    MemoryStream stream = new(original);

    Stream result = await _encryption.EncryptAsync(stream, "");

    Assert.Same(stream, result);
  }

  [Fact]
  public async Task Decrypt_WithEmptyPassword_ReturnsOriginal()
  {
    byte[] original = "No decryption needed"u8.ToArray();
    MemoryStream stream = new(original);

    Stream result = await _encryption.DecryptAsync(stream, "");

    Assert.Same(stream, result);
  }
}