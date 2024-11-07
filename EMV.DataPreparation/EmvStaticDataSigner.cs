using EMV.DataPreparation;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

public class EmvStaticDataSigner
{
    private readonly ILogger<EmvStaticDataSigner> _logger;
    private const string DEFAULT_DAC = "DAC1";

    public class SignedDataResult
    {
        public bool Success { get; set; }
        public byte[] SignedData { get; set; }
        public string ErrorMessage { get; set; }
    }

    public SignedDataResult SignStaticApplicationData(
        byte[] staticData,
        string issuerModulus,
        string issuerPrivateExponent)
    {
        try
        {
            _logger?.LogInformation("Starting Static Application Data signing");

            // Validate inputs
            if (staticData == null || staticData.Length == 0)
                throw new ArgumentException("Static data is required");

            if (string.IsNullOrEmpty(issuerModulus))
                throw new ArgumentException("Issuer modulus is required");

            if (string.IsNullOrEmpty(issuerPrivateExponent))
                throw new ArgumentException("Issuer private exponent is required");

            // Convert issuer key components
            byte[] modulusBytes = EmvRsaHelper.HexStringToByteArray(issuerModulus);
            byte[] privateExpBytes = EmvRsaHelper.HexStringToByteArray(issuerPrivateExponent);

            // Calculate issuer key length in bytes
            int issuerKeyLength = modulusBytes.Length;
            _logger?.LogInformation($"Issuer key length: {issuerKeyLength} bytes");

            // Create buffer for signed data
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                // 1. Header
                writer.Write((byte)0x6A);
                _logger?.LogDebug("Added Header: 6A");

                // 2. Format
                writer.Write((byte)0x03);  // Format for Static Data
                _logger?.LogDebug("Added Format: 03");

                // 3. Hash Algorithm Indicator
                writer.Write((byte)0x01);  // SHA-1
                _logger?.LogDebug("Added Hash Algorithm: 01");

                // 4. DAC
                byte[] dacBytes = Encoding.ASCII.GetBytes(DEFAULT_DAC);
                writer.Write(dacBytes);
                _logger?.LogDebug($"Added DAC: {DEFAULT_DAC}");

                // 5. Calculate and add padding
                int paddingLength = issuerKeyLength - 26;  // Same as original code
                _logger?.LogDebug($"Padding length: {paddingLength} bytes");

                for (int i = 0; i < paddingLength; i++)
                {
                    writer.Write((byte)0xBB);
                }

                // 6. Add Static Application Data
                writer.Write(staticData);
                _logger?.LogDebug($"Added static data, length: {staticData.Length} bytes");

                // Get data for hash calculation
                byte[] dataToHash = ms.ToArray();
                _logger?.LogDebug($"Data to hash length: {dataToHash.Length} bytes");

                // 7. Calculate and add hash
                byte[] hash;
                using (var sha1 = SHA1.Create())
                {
                    hash = sha1.ComputeHash(dataToHash);
                }
                writer.Write(hash);
                _logger?.LogDebug($"Added hash: {BitConverter.ToString(hash)}");

                // 8. Add trailer
                writer.Write((byte)0xBC);
                _logger?.LogDebug("Added Trailer: BC");

                // Get complete data block
                byte[] completeData = ms.ToArray();
                _logger?.LogInformation($"Complete data block length: {completeData.Length} bytes");

                // Perform RSA signing using issuer private key
                var rsaParams = new RSAParameters
                {
                    Modulus = modulusBytes,
                    D = privateExpBytes,
                    Exponent = new byte[] { 0x03 }
                };

                // Sign data
                byte[] signedData = SignWithRsa(completeData, rsaParams);

                return new SignedDataResult
                {
                    Success = true,
                    SignedData = signedData
                };
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to sign static application data");
            return new SignedDataResult
            {
                Success = false,
                ErrorMessage = $"Signing failed: {ex.Message}"
            };
        }
    }

    private byte[] SignWithRsa(byte[] data, RSAParameters keyParams)
    {
        try
        {
            _logger?.LogDebug("Starting RSA signing operation");

            using (var rsa = new RSACryptoServiceProvider(keyParams.Modulus.Length * 8))
            {
                rsa.ImportParameters(keyParams);

                // Perform raw RSA operation
                var message = new BigInteger(data.Reverse().ToArray());
                var modulus = new BigInteger(keyParams.Modulus.Reverse().ToArray());
                var privateExp = new BigInteger(keyParams.D.Reverse().ToArray());

                var result = BigInteger.ModPow(message, privateExp, modulus);
                var signature = result.ToByteArray().Reverse().ToArray();

                // Ensure correct length
                if (signature.Length < keyParams.Modulus.Length)
                {
                    var padded = new byte[keyParams.Modulus.Length];
                    Buffer.BlockCopy(signature, 0, padded,
                        padded.Length - signature.Length, signature.Length);
                    signature = padded;
                }

                _logger?.LogDebug($"Signature generated, length: {signature.Length} bytes");
                return signature;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "RSA signing operation failed");
            throw new CryptographicException("Failed to sign data with RSA", ex);
        }
    }
}
