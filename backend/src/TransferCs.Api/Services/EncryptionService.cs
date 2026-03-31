using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Org.BouncyCastle.Security;

namespace TransferCs.Api.Services;

public static class EncryptionService
{
    public static async Task<Stream> EncryptAsync(Stream plaintext, string password)
    {
        if (string.IsNullOrEmpty(password))
            return plaintext;

        var output = new MemoryStream();

        // Read the plaintext into a byte array for PGP literal data
        using var plaintextMs = new MemoryStream();
        await plaintext.CopyToAsync(plaintextMs);
        var plaintextBytes = plaintextMs.ToArray();

        await using (var armoredStream = new ArmoredOutputStream(output))
        {
            var encGen = new PgpEncryptedDataGenerator(
                SymmetricKeyAlgorithmTag.Aes256, true, new SecureRandom());
            encGen.AddMethod(password.ToCharArray(), HashAlgorithmTag.Sha256);

            // Build the literal data into a buffer first
            using var literalBuffer = new MemoryStream();
            var literalGen = new PgpLiteralDataGenerator();
            await using (var literalOut = literalGen.Open(literalBuffer, PgpLiteralData.Binary, "_", DateTime.UtcNow, new byte[4096]))
            {
                await literalOut.WriteAsync(plaintextBytes);
            }

            var literalBytes = literalBuffer.ToArray();

            await using (var encOut = encGen.Open(armoredStream, literalBytes.Length))
            {
                await encOut.WriteAsync(literalBytes);
            }
        }

        output.Position = 0;
        return output;
    }

    public static async Task<Stream> DecryptAsync(Stream ciphertext, string password)
    {
        if (string.IsNullOrEmpty(password))
            return ciphertext;

        await using var armoredStream = new ArmoredInputStream(ciphertext);
        var factory = new PgpObjectFactory(armoredStream);
        var encList = (PgpEncryptedDataList)factory.NextPgpObject();
        var pbeData = (PgpPbeEncryptedData)encList[0];

        await using var clear = pbeData.GetDataStream(password.ToCharArray());
        var plainFactory = new PgpObjectFactory(clear);
        var literalData = (PgpLiteralData)plainFactory.NextPgpObject();

        var output = new MemoryStream();
        await using (var unc = literalData.GetInputStream())
        {
            await unc.CopyToAsync(output);
        }

        output.Position = 0;
        return output;
    }
}
