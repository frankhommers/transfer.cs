using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Org.BouncyCastle.Security;
using TransferCs.Api.Storage;

namespace TransferCs.Api.Services;

public sealed class EncryptionService(DiskSpaceGuard diskSpace)
{
  /// <summary>
  /// Encrypts plaintext stream using PGP symmetric encryption.
  /// Uses a temp file to avoid holding the entire file in memory.
  /// </summary>
  public async Task<Stream> EncryptAsync(Stream plaintext, string password)
  {
    if (string.IsNullOrEmpty(password))
      return plaintext;

    Stream tempFile = diskSpace.CreateTemporaryFile("encrypt");

    try
    {
      // Note: ArmoredOutputStream does not close the underlying stream on dispose
      await using (ArmoredOutputStream armoredStream = new(tempFile))
      {
        PgpEncryptedDataGenerator encGen = new(
          SymmetricKeyAlgorithmTag.Aes256, true, new SecureRandom());
        encGen.AddMethod(password.ToCharArray(), HashAlgorithmTag.Sha256);

        await using (Stream encOut = encGen.Open(armoredStream, new byte[81920]))
        {
          PgpLiteralDataGenerator literalGen = new();
          await using (Stream literalOut =
            literalGen.Open(encOut, PgpLiteralData.Binary, "_", DateTime.UtcNow, new byte[81920]))
          {
            await plaintext.CopyToAsync(literalOut);
          }
        }
      }

      tempFile.Position = 0;
      return tempFile;
    }
    catch
    {
      await tempFile.DisposeAsync();
      throw;
    }
  }

  /// <summary>
  /// Decrypts a PGP-encrypted stream using the password.
  /// Uses a temp file to avoid holding the entire file in memory.
  /// </summary>
  public async Task<Stream> DecryptAsync(Stream ciphertext, string password)
  {
    if (string.IsNullOrEmpty(password))
      return ciphertext;

    await using ArmoredInputStream armoredStream = new(ciphertext);
    PgpObjectFactory factory = new(armoredStream);
    PgpEncryptedDataList encList = (PgpEncryptedDataList)factory.NextPgpObject();
    PgpPbeEncryptedData pbeData = (PgpPbeEncryptedData)encList[0];

    await using Stream clear = pbeData.GetDataStream(password.ToCharArray());
    PgpObjectFactory plainFactory = new(clear);
    PgpLiteralData literalData = (PgpLiteralData)plainFactory.NextPgpObject();

    Stream tempFile = diskSpace.CreateTemporaryFile("decrypt");

    try
    {
      await using (Stream unc = literalData.GetInputStream())
      {
        await unc.CopyToAsync(tempFile);
      }

      tempFile.Position = 0;
      return tempFile;
    }
    catch
    {
      await tempFile.DisposeAsync();
      throw;
    }
  }
}
