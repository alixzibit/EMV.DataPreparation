using EMV.DataPreparation;
using System;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;


public class EmvCertificateBuilder
{
    public static byte[] BuildAndSignIssuerCertificate(
        string issuerIdentifier,
        string expiryDate,
        string issuerModulus,
        string issuerExponent,
        RSAParameters caKey)
    {
        // 1. Build Certificate Data
        var certData = BuildIssuerCertificateData(
            issuerIdentifier,
            expiryDate,
            issuerModulus,
            issuerExponent);

        Console.WriteLine($"Built certificate data: {BitConverter.ToString(certData)}");

        // 2. Add Hash
        var withHash = AddHash(certData);
        Console.WriteLine($"Certificate with hash length: {withHash.Length}");

        // 3. Sign with CA Private Key
        return SignCertificate(withHash, caKey);
    }

    private static byte[] BuildIssuerCertificateData(
        string issuerIdentifier,
        string expiryDate,
        string issuerModulus,
        string issuerExponent)
    {
        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            // Header
            writer.Write((byte)0x6A);
            writer.Write((byte)0x02);  // Certificate Format

            // Issuer Identifier (padded to 4 bytes)
            var idBytes = HexStringToByteArray(issuerIdentifier.PadRight(8, 'F'));
            writer.Write(idBytes);

            // Certificate Expiry Date (YYMM)
            var expiryBytes = HexStringToByteArray(expiryDate);
            writer.Write(expiryBytes);

            // Serial Number (3 bytes)
            writer.Write(HexStringToByteArray("000001"));

            // Hash Algorithm Indicator
            writer.Write((byte)0x01);  // SHA-1

            // Issuer Public Key Algorithm
            writer.Write((byte)0x01);  // RSA

            // Issuer Public Key Length
            var modulusBytes = HexStringToByteArray(issuerModulus);
            writer.Write((byte)modulusBytes.Length);

            // Issuer Public Key Exponent Length
            var exponentBytes = HexStringToByteArray(issuerExponent);
            writer.Write((byte)exponentBytes.Length);

            // Issuer Public Key
            writer.Write(modulusBytes);

            return ms.ToArray();
        }
    }

    private static byte[] AddHash(byte[] certData)
    {
        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            // Write original data
            writer.Write(certData);

            // Calculate and write hash
            using (var sha1 = SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(certData);
                writer.Write(hash);
            }

            // Add trailer
            writer.Write((byte)0xBC);

            return ms.ToArray();
        }
    }

    private static byte[] SignCertificate(byte[] data, RSAParameters privateKey)
    {
        try
        {
            // 1. Apply EMV padding
            byte[] paddedData = ApplyEmvPadding(data, privateKey.Modulus.Length);

            // 2. Perform raw RSA operation using BigInteger
            var message = new BigInteger(paddedData.Reverse().ToArray());
            var modulus = new BigInteger(privateKey.Modulus.Reverse().ToArray());
            var exponent = new BigInteger(privateKey.Exponent.Reverse().ToArray());

            // 3. c = m^e mod n
            var result = BigInteger.ModPow(message, exponent, modulus);

            // 4. Convert back to byte array
            byte[] signature = result.ToByteArray().Reverse().ToArray();

            // 5. Ensure correct length
            if (signature.Length < privateKey.Modulus.Length)
            {
                var padded = new byte[privateKey.Modulus.Length];
                Buffer.BlockCopy(signature, 0, padded,
                    padded.Length - signature.Length, signature.Length);
                signature = padded;
            }

            return signature;
        }
        catch (Exception ex)
        {
            throw new CertificateGenerationException(
                "Failed to sign certificate", ex);
        }
    }

    private static byte[] ApplyEmvPadding(byte[] data, int modulusLength)
    {
        byte[] paddedBlock = new byte[modulusLength];

        // EMV padding: 0x00 || 0x01 || PS || 0x00 || DATA
        paddedBlock[0] = 0x00;
        paddedBlock[1] = 0x01;

        int dataOffset = modulusLength - data.Length;

        // Fill PS with 0xFF
        for (int i = 2; i < dataOffset - 1; i++)
        {
            paddedBlock[i] = 0xFF;
        }

        // Add separator
        paddedBlock[dataOffset - 1] = 0x00;

        // Copy data
        Buffer.BlockCopy(data, 0, paddedBlock, dataOffset, data.Length);

        return paddedBlock;
    }

    private static byte[] HexStringToByteArray(string hex)
    {
        hex = hex.Replace(" ", "").Replace("-", "");
        byte[] bytes = new byte[hex.Length / 2];
        for (int i = 0; i < hex.Length; i += 2)
        {
            bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
        }
        return bytes;
    }
}

