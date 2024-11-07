
using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace EMV.DataPreparation
{
    public class EmvRsaHelper
    {
        private const int PKCS1_HEADER_LENGTH = 3;  // 00 01 FF
        private const byte PKCS1_SIGNATURE_BLOCK = 0x01;
        // qSparc standard key lengths
        private const int CA_KEY_LENGTH = 1984;     // bits
        private const int ISSUER_KEY_LENGTH = 1408; // bits
        private const int ICC_KEY_LENGTH = 768;     // bits

        public static byte[] SignEmvData(byte[] data, RSAParameters rsaParams)
        {
            try
            {
                using (var rsa = new RSACryptoServiceProvider(rsaParams.Modulus.Length * 8))
                {
                    rsa.ImportParameters(rsaParams);

                    // Apply EMV-specific padding manually
                    byte[] paddedData = ApplyEmvPadding(data, rsaParams.Modulus.Length);

                    // Use PKCS1 padding as it's the closest to what we need
                    return rsa.Encrypt(paddedData, RSAEncryptionPadding.Pkcs1);
                }
            }
            catch (CryptographicException ex)
            {
                throw new CertificateGenerationException("RSA signing operation failed", ex);
            }
        }

        public static byte[] ApplyEmvPadding(byte[] data, int modulusLength)
        {
            // Calculate available space for data
            int maxDataLength = modulusLength - PKCS1_HEADER_LENGTH - 1; // -1 for the 0x00 separator

            if (data.Length > maxDataLength)
            {
                throw new ArgumentException(
                    $"Data too long for RSA modulus. " +
                    $"Data length: {data.Length}, " +
                    $"Max allowed: {maxDataLength} " +
                    $"for modulus length: {modulusLength}");
            }

            byte[] paddedBlock = new byte[modulusLength];

            // EMV padding structure:
            // [00] [01] [FF..FF] [00] [data]
            paddedBlock[0] = 0x00;
            paddedBlock[1] = PKCS1_SIGNATURE_BLOCK;

            // Fill with 0xFF
            int paddingLength = modulusLength - data.Length - PKCS1_HEADER_LENGTH;
            for (int i = 2; i < paddingLength + 2; i++)
            {
                paddedBlock[i] = 0xFF;
            }

            // Add separator
            paddedBlock[paddingLength + 2] = 0x00;

            // Copy data at the end
            Buffer.BlockCopy(data, 0, paddedBlock, modulusLength - data.Length, data.Length);

            return paddedBlock;
        }

        //public static byte[] SignCertificate(byte[] data, RSAParameters privateKey)
        //{
        //    try
        //    {
        //        // 1. Apply EMV padding
        //        byte[] paddedData = ApplyEmvPadding(data, privateKey.Modulus.Length);

        //        // 2. Perform raw RSA operation using BigInteger
        //        var message = new BigInteger(paddedData.Reverse().ToArray());
        //        var modulus = new BigInteger(privateKey.Modulus.Reverse().ToArray());
        //        var exponent = new BigInteger(privateKey.Exponent.Reverse().ToArray());

        //        // 3. c = m^e mod n
        //        var result = BigInteger.ModPow(message, exponent, modulus);

        //        // 4. Convert back to byte array
        //        byte[] signature = result.ToByteArray().Reverse().ToArray();

        //        // 5. Ensure correct length
        //        if (signature.Length < privateKey.Modulus.Length)
        //        {
        //            var padded = new byte[privateKey.Modulus.Length];
        //            Buffer.BlockCopy(signature, 0, padded,
        //                padded.Length - signature.Length, signature.Length);
        //            signature = padded;
        //        }

        //        return signature;
        //    }
        //    catch (Exception ex)
        //    {
        //        throw new CertificateGenerationException(
        //            "Failed to sign certificate", ex);
        //    }
        //}

        //public static byte[] SignCertificate(byte[] data, RSAParameters privateKey)
        //{
        //    try
        //    {
        //        Debug.WriteLine($"Data length to sign: {data.Length} bytes");
        //        Debug.WriteLine($"Modulus length: {privateKey.Modulus.Length} bytes");

        //        // For EMV, we need to perform raw RSA operation
        //        // c = m^d mod n

        //        // 1. Convert data to BigInteger (reverse for big-endian)
        //        var message = new BigInteger(data.Reverse().ToArray());

        //        // 2. Get modulus and private exponent
        //        var modulus = new BigInteger(privateKey.Modulus.Reverse().ToArray());
        //        var privateExp = new BigInteger(privateKey.D.Reverse().ToArray());

        //        // 3. Perform RSA operation
        //        var result = BigInteger.ModPow(message, privateExp, modulus);

        //        // 4. Convert back to byte array
        //        byte[] signature = result.ToByteArray().Reverse().ToArray();

        //        // 5. Ensure correct length
        //        if (signature.Length < privateKey.Modulus.Length)
        //        {
        //            var padded = new byte[privateKey.Modulus.Length];
        //            Buffer.BlockCopy(signature, 0, padded,
        //                padded.Length - signature.Length, signature.Length);
        //            signature = padded;
        //        }

        //        return signature;
        //    }
        //    catch (Exception ex)
        //    {
        //        throw new CertificateGenerationException("Failed to sign certificate", ex);
        //    }
        //}

        private static byte[] SignCertificate(byte[] data, RSAParameters signingKey)
        {
            try
            {
                //_logger?.LogDebug($"Raw signing data length: {data.Length} bytes");

                // 1. Apply EMV padding directly
                byte[] paddedData = ApplyEmvPadding(data, signingKey.Modulus.Length);
                //_logger?.LogDebug($"Padded data length: {paddedData.Length} bytes");

                // 2. Perform RSA operation
                return PerformRsaOperation(paddedData, signingKey);
            }
            catch (Exception ex)
            {
                throw new CertificateGenerationException("Failed to sign certificate", ex);
            }
        }

        public static byte[] SignCertificateData(byte[] data, RSAParameters signingKey, bool isManualExponent = false)
        {   
            try
            {
                //_logger?.LogInformation($"Signing certificate data, length: {data.Length} bytes");
                //_logger?.LogInformation($"Using manual exponent: {isManualExponent}");

                if (isManualExponent)
                {
                    // Use FormatAndSignCertificate for manually provided exponent
                    return FormatAndSignCertificate(data, signingKey);
                }
                else
                {
                    // Use direct SignCertificate for loaded keys
                    return SignCertificate(data, signingKey);
                }
            }
            catch (Exception ex)
            {
                throw new CertificateGenerationException($"Failed to sign certificate: {ex.Message}", ex);
            }
        }

        private static byte[] PerformRsaOperation(byte[] paddedData, RSAParameters key)
        {
            try
            {
                // Use BigInteger for raw RSA operation
                var message = new BigInteger(paddedData.Reverse().ToArray());
                var modulus = new BigInteger(key.Modulus.Reverse().ToArray());
                var exponent = new BigInteger(key.D.Reverse().ToArray()); // Private exponent

                // Perform c = m^d mod n
                var result = BigInteger.ModPow(message, exponent, modulus);

                // Convert back to byte array
                byte[] signature = result.ToByteArray().Reverse().ToArray();

                // Ensure correct length
                if (signature.Length < key.Modulus.Length)
                {
                    byte[] padded = new byte[key.Modulus.Length];
                    Buffer.BlockCopy(signature, 0, padded,
                        padded.Length - signature.Length, signature.Length);
                    signature = padded;
                }

                //_logger?.LogDebug($"Generated signature length: {signature.Length} bytes");
                return signature;
            }
            catch (Exception ex)
            {
                throw new CertificateGenerationException("RSA operation failed", ex);
            }
        }

        //public static byte[] EncryptIssuerCertificate(byte[] certData, RSAParameters caKey)
        //{
        //    try
        //    {
        //        // Calculate and log sizes
        //        int modulusLength = caKey.Modulus.Length;
        //        int certLength = certData.Length;
        //        int requiredLength = certLength + PKCS1_HEADER_LENGTH;

        //        Debug.WriteLine($"Certificate data length: {certLength} bytes");
        //        Debug.WriteLine($"CA Modulus length: {modulusLength} bytes");
        //        Debug.WriteLine($"Required length with padding: {requiredLength} bytes");
        //        ValidateKeyParameters(caKey);
        //        using (var rsa = new RSACryptoServiceProvider(modulusLength * 8))
        //        {
        //            // Convert exponent if needed (03 hex to integer)
        //            if (caKey.Exponent.Length == 1 && caKey.Exponent[0] == 0x03)
        //            {
        //                // Keep using 03 as it's valid for EMV
        //                Debug.WriteLine("Using EMV standard exponent (03)");
        //            }
        //            else
        //            {
        //                Debug.WriteLine($"Using custom exponent: {BitConverter.ToString(caKey.Exponent)}");
        //            }

        //            rsa.ImportParameters(caKey);

        //            // Apply EMV padding
        //            byte[] paddedData = ApplyEmvPadding(certData, modulusLength);
        //            Debug.WriteLine($"Padded data length: {paddedData.Length} bytes");

        //            // Encrypt with PKCS1 padding
        //            return rsa.Encrypt(paddedData, RSAEncryptionPadding.Pkcs1);
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        throw new CertificateGenerationException(
        //            $"Failed to encrypt issuer certificate: {ex.Message}\n" +
        //            $"Cert Data Length: {certData.Length}, " +
        //            $"Modulus Length: {caKey.Modulus.Length}", ex);
        //    }
        //}

        public static byte[] EncryptIssuerCertificate(byte[] certData, RSAParameters key)
        {
            try
            {
                // EMV uses raw RSA encryption (no padding scheme)
                var message = new BigInteger(certData.Reverse().ToArray());
                var modulus = new BigInteger(key.Modulus.Reverse().ToArray());
                var exponent = new BigInteger(key.Exponent.Reverse().ToArray());

                var result = BigInteger.ModPow(message, exponent, modulus);
                return result.ToByteArray().Reverse().ToArray();
            }
            catch (Exception ex)
            {
                throw new CertificateGenerationException(
                    $"Failed to encrypt issuer certificate: {ex.Message}. " +
                    $"Cert Data Length: {certData.Length}, Modulus Length: {key.Modulus.Length}",
                    ex);
            }
        }

        public static byte[] FormatAndSignCertificate(byte[] data, RSAParameters signingKey)
        {
            try
            {
                int modulusLength = signingKey.Modulus.Length;
                Debug.WriteLine($"Modulus length: {modulusLength} bytes");
                Debug.WriteLine($"Input data length: {data.Length} bytes");

                // 1. Hash the data
                byte[] hash;
                using (var sha1 = SHA1.Create())
                {
                    hash = sha1.ComputeHash(data);
                }
                Debug.WriteLine($"Hash length: {hash.Length} bytes");

                // 2. Apply DigestInfo encoding
                byte[] digestInfo = FormatDigestInfo(hash);
                Debug.WriteLine($"DigestInfo length: {digestInfo.Length} bytes");

                // 3. Apply EMV padding
                byte[] paddedData = ApplyEmvPadding(digestInfo, modulusLength);
                Debug.WriteLine($"Padded data length: {paddedData.Length} bytes");

                // 4. Perform RSA operation
                return SignCertificate(paddedData, signingKey);
            }
            catch (Exception ex)
            {
                throw new CertificateGenerationException("Failed to format and sign certificate", ex);
            }
        }

        //public static byte[] FormatAndSignCertificate(byte[] data, RSAParameters signingKey)
        //{
        //    try
        //    {
        //        int modulusLength = signingKey.Modulus.Length;
        //        //_logger?.LogDebug($"Modulus length: {modulusLength} bytes");
        //        //_logger?.LogDebug($"Input data length: {data.Length} bytes");

        //        // 1. Hash the data
        //        byte[] hash;
        //        using (var sha1 = SHA1.Create())
        //        {
        //            hash = sha1.ComputeHash(data);
        //        }
        //        //_logger?.LogDebug($"Hash length: {hash.Length} bytes");

        //        // 2. Apply DigestInfo encoding
        //        byte[] digestInfo = FormatDigestInfo(hash);
        //        //_logger?.LogDebug($"DigestInfo length: {digestInfo.Length} bytes");

        //        // 3. Apply EMV padding
        //        byte[] paddedData = ApplyEmvPadding(digestInfo, modulusLength);
        //        //_logger?.LogDebug($"Padded data length: {paddedData.Length} bytes");

        //        // 4. Perform RSA operation
        //        return PerformRsaOperation(paddedData, signingKey);
        //    }
        //    catch (Exception ex)
        //    {
        //        throw new CertificateGenerationException("Failed to format and sign certificate", ex);
        //    }
        //}

        //private static byte[] FormatDigestInfo(byte[] hash)
        //{
        //    // DigestInfo structure for SHA-1:
        //    // 0x30 [length] 0x30 0x0D 0x06 0x09 0x60 0x86 0x48 0x01 0x65 0x03 0x04 0x02 0x01 0x05 0x00 0x04 [hash]
        //    byte[] digestInfo = new byte[hash.Length + 15];
        //    digestInfo[0] = 0x30; // Sequence
        //    digestInfo[1] = (byte)(digestInfo.Length - 2); // Length
        //    digestInfo[2] = 0x30; // Sequence
        //    digestInfo[3] = 0x0D; // Length
        //    digestInfo[4] = 0x06; // OID
        //    digestInfo[5] = 0x09; // Length
        //    digestInfo[6] = 0x60; // OID
        //    digestInfo[7] = 0x86; // OID
        //    digestInfo[8] = 0x48; // OID
        //    digestInfo[9] = 0x01; // OID
        //    digestInfo[10] = 0x65; // OID
        //    digestInfo[11] = 0x03; // OID
        //    digestInfo[12] = 0x04; // OID
        //    digestInfo[13] = 0x02; // OID
        //    digestInfo[14] = 0x01; // OID
        //    digestInfo[15] = 0x05; // OID
        //    digestInfo[16] = 0x00; // NULL
        //    digestInfo[17] = 0x04; // Octet string
        //    Buffer.BlockCopy(hash, 0, digestInfo, 18, hash.Length);
        //    return digestInfo;
        //}

        private static byte[] FormatDigestInfo(byte[] hash)
        {
            // DigestInfo for SHA-1
            byte[] prefix = new byte[]
            {
            0x30, 0x21, 0x30, 0x09, 0x06, 0x05, 0x2B, 0x0E,
            0x03, 0x02, 0x1A, 0x05, 0x00, 0x04, 0x14
            };

            var digestInfo = new byte[prefix.Length + hash.Length];
            Buffer.BlockCopy(prefix, 0, digestInfo, 0, prefix.Length);
            Buffer.BlockCopy(hash, 0, digestInfo, prefix.Length, hash.Length);

            return digestInfo;
        }

        private static void ValidateKeyParameters(RSAParameters key)
        {
            // EMV standard key lengths
            const int CA_KEY_LENGTH_1984 = 248;     // 1984 bits = 248 bytes
            const int ISSUER_KEY_LENGTH_1408 = 176; // 1408 bits = 176 bytes
            const int ICC_KEY_LENGTH_768 = 96;      // 768 bits = 96 bytes

            int modulusLength = key.Modulus.Length;

            // Validate modulus length
            if (modulusLength != CA_KEY_LENGTH_1984 &&
                modulusLength != ISSUER_KEY_LENGTH_1408 &&
                modulusLength != ICC_KEY_LENGTH_768)
            {
                throw new ArgumentException(
                    $"Invalid key length. Expected 1984, 1408, or 768 bits. Got {modulusLength * 8} bits.");
            }

            // Validate exponent (03 or 65537 are common)
            if (key.Exponent.Length == 1 && key.Exponent[0] == 0x03)
            {
                // Valid EMV exponent
            }
            else if (key.Exponent.Length == 3 &&
                     key.Exponent[0] == 0x01 &&
                     key.Exponent[1] == 0x00 &&
                     key.Exponent[2] == 0x01)
            {
                // Valid alternate exponent (65537)
            }
            else
            {
                throw new ArgumentException("Invalid public exponent. Expected 03 or 010001");
            }
        }


        public static byte[] EncryptIccCertificate(byte[] certData, RSAParameters issuerKey)
        {
            try
            {
                using (var rsa = new RSACryptoServiceProvider(issuerKey.Modulus.Length * 8))
                {
                    rsa.ImportParameters(issuerKey);

                    // Apply padding and encrypt
                    byte[] paddedData = ApplyEmvPadding(certData, issuerKey.Modulus.Length);
                    return rsa.Encrypt(paddedData, RSAEncryptionPadding.Pkcs1);
                }
            }
            catch (Exception ex)
            {
                throw new CertificateGenerationException("Failed to encrypt ICC certificate", ex);
            }
        }

        public static bool VerifyCertificate(byte[] data, byte[] signature, RSAParameters publicKey)
        {
            try
            {
                using (var rsa = new RSACryptoServiceProvider(publicKey.Modulus.Length * 8))
                {
                    rsa.ImportParameters(publicKey);

                    // Decrypt signature
                    byte[] decrypted = rsa.Decrypt(signature, RSAEncryptionPadding.Pkcs1);

                    // Verify EMV padding format
                    if (decrypted[0] != 0x00 || decrypted[1] != 0x01)
                        return false;

                    // Find the 0x00 separator after the 0xFF padding
                    int separatorIndex = 2;
                    while (separatorIndex < decrypted.Length && decrypted[separatorIndex] == 0xFF)
                        separatorIndex++;

                    if (separatorIndex >= decrypted.Length - 1 || decrypted[separatorIndex] != 0x00)
                        return false;

                    separatorIndex++; // Move past the separator

                    // Extract and compare the original data
                    byte[] extractedData = new byte[decrypted.Length - separatorIndex];
                    Buffer.BlockCopy(decrypted, separatorIndex, extractedData, 0, extractedData.Length);

                    return data.SequenceEqual(extractedData);
                }
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        public static RSAParameters CreateRsaParameters(string modulus, string exponent)
        {
            var parameters = new RSAParameters
            {
                Modulus = HexStringToByteArray(modulus),
                Exponent = HexStringToByteArray(exponent)
            };

            // Log parameters
            Debug.WriteLine($"Creating RSA parameters:");
            Debug.WriteLine($"Modulus length: {parameters.Modulus.Length} bytes");
            Debug.WriteLine($"Exponent: {BitConverter.ToString(parameters.Exponent)}");

            // Validate
            ValidateKeyParameters(parameters);

            return parameters;
        }

        public static byte[] HexStringToByteArray(string hex)
        {
            hex = hex.Replace(" ", "").Replace("-", "");

            if (hex.Length % 2 != 0)
                throw new ArgumentException("Hex string must have an even length");

            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < hex.Length; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            }
            return bytes;
        }
    }

    //public class CertificateGenerationException : Exception
    //{
    //    public CertificateGenerationException(string message) : base(message) { }
    //    public CertificateGenerationException(string message, Exception inner) : base(message, inner) { }
    //}
}
