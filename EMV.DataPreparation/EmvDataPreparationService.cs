using System;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Multos.Crypto.Core;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace EMV.DataPreparation
{
    public class EmvDataPreparationService
    {
        private readonly ILogger<EmvDataPreparationService> _logger;
        private const int SHA1_LENGTH = 20;
        private const byte CERTIFICATE_HEADER = 0x6A;
        private const byte CERTIFICATE_TRAILER = 0xBC;
        private readonly EmvKeyLoader _keyLoader;

        public EmvDataPreparationService()
        {
            _keyLoader = new EmvKeyLoader();
        }


        public EmvDataPreparationService(ILogger<EmvDataPreparationService> logger = null)
        {
            if (logger == null)
            {
                var logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs.txt");
                var loggerFactory = LoggerFactory.Create(builder =>
                {
                    builder.AddProvider(new FileLoggerProvider(logFilePath));
                });
                _logger = loggerFactory.CreateLogger<EmvDataPreparationService>();
            }
            else
            {
                _logger = logger;
            }
        }

        public class CertificateBuilder
        {
            private readonly MemoryStream _stream;
            private readonly BinaryWriter _writer;

            public CertificateBuilder()
            {
                _stream = new MemoryStream();
                _writer = new BinaryWriter(_stream);
            }

            public void AddByte(byte value)
            {
                _writer.Write(value);
            }

            public void AddBytes(byte[] data)
            {
                if (data != null && data.Length > 0)
                {
                    _writer.Write(data);
                }
            }

            public void AddHexString(string hex)
            {
                if (!string.IsNullOrEmpty(hex))
                {
                    AddBytes(EmvRsaHelper.HexStringToByteArray(hex));
                }
            }

            public byte[] GetCertificateData()
            {
                _writer.Flush();
                byte[] data = _stream.ToArray();

                if (data.Length == 0)
                {
                    throw new CertificateGenerationException("Certificate data is empty");
                }

                return data;
            }

            public int GetLength()
            {
                return (int)_stream.Length;
            }
        }

        public static class IssuerCertificateBuilder
        {
            public static byte[] BuildIssuerCertificate(
                string issuerIdentifier,
                string expiryDate,
                string issuerModulus,
                string issuerExponent)
            {
                var builder = new CertificateBuilder();

                // 1. Certificate Header (1 byte)
                builder.AddByte(0x6A);  // Header

                // 2. Certificate Format (1 byte)
                builder.AddByte(0x02);

                // 3. Issuer Identifier (4 bytes) - padded with 'F'
                string paddedIssuerIdentifier = issuerIdentifier.PadRight(8, 'F');
                builder.AddHexString(paddedIssuerIdentifier);

                // 4. Certificate Expiry Date (2 bytes - MMYY)
                builder.AddHexString(expiryDate);

                // 5. Certificate Serial Number (3 bytes)
                builder.AddHexString("000001");  // Standard serial for issuer cert

                // 6. Hash Algorithm Indicator (1 byte)
                builder.AddByte(0x01);  // SHA-1

                // 7. Issuer Public Key Algorithm Indicator (1 byte)
                builder.AddByte(0x01);  // RSA

                // 8. Issuer Public Key Length (1 byte)
                byte[] modulusBytes = EmvRsaHelper.HexStringToByteArray(issuerModulus);
                builder.AddByte((byte)modulusBytes.Length);

                // 9. Issuer Public Key Exponent Length (1 byte)
                byte[] exponentBytes = EmvRsaHelper.HexStringToByteArray(issuerExponent);
                builder.AddByte((byte)exponentBytes.Length);

                // 10. Issuer Public Key or Leftmost Digits
                builder.AddBytes(modulusBytes);

                // 11. Hash
                byte[] dataToHash = builder.GetCertificateData();
                using (var sha1 = SHA1.Create())
                {
                    byte[] hash = sha1.ComputeHash(dataToHash);
                    builder.AddBytes(hash);
                }

                // 12. Trailer
                builder.AddByte(0xBC);

                return builder.GetCertificateData();
            }
        }

        //private async Task<QSparcKeyGenerator.KeyGenerationResult> GenerateCardKeys(
        //    CardInput cardInput,
        //    bool useExistingKey = false)
        //{
        //    try
        //    {
        //        _logger?.LogInformation($"Generating card keys with key length: {cardInput.IccKeyLength}");

        //        // Validate master keys
        //        if (cardInput.MasterKeys == null)
        //        {
        //            throw new ArgumentException("Master keys are required");
        //        }

        //        ValidateMasterKeys(cardInput.MasterKeys);

        //        // Prepare key derivation data if needed
        //        KeyDerivationData derivationData = null;
        //        if (cardInput.DerivationOption != KeyDerivationOption.OptionA)
        //        {
        //            derivationData = new KeyDerivationData
        //            {
        //                Pan = cardInput.Pan,
        //                Psn = cardInput.Psn,
        //                UseZeroPsn = false // Can be made configurable if needed
        //            };
        //        }

        //        // Generate or load keys based on configuration
        //        QSparcKeyGenerator.KeyGenerationResult result;

        //        if (!useExistingKey)
        //        {
        //            _logger?.LogInformation("Generating new ICC RSA key pair");

        //            // Generate new keys
        //            result = QSparcKeyGenerator.GenerateQSparcKeys(
        //                imkAc: cardInput.MasterKeys.ImkAc,
        //                imkSmi: cardInput.MasterKeys.ImkSmi,
        //                imkSmc: cardInput.MasterKeys.ImkSmc,
        //                derivationData: derivationData,
        //                option: cardInput.DerivationOption,
        //                rsaKeySize: cardInput.IccKeyLength);
        //        }
        //        else
        //        {
        //            _logger?.LogInformation("Using existing keys without derivation");

        //            // Use existing keys without derivation
        //            result = QSparcKeyGenerator.GenerateQSparcKeysNoDiversification(
        //                imkAc: cardInput.MasterKeys.ImkAc,
        //                imkSmi: cardInput.MasterKeys.ImkSmi,
        //                imkSmc: cardInput.MasterKeys.ImkSmc,
        //                rsaKeySize: cardInput.IccKeyLength);
        //        }

        //        if (!result.Success)
        //        {
        //            _logger?.LogError($"Key generation failed: {result.ErrorMessage}");
        //            throw new KeyGenerationException($"Failed to generate keys: {result.ErrorMessage}");
        //        }

        //        // Validate generated keys
        //        ValidateGeneratedKeys(result);

        //        _logger?.LogInformation("Successfully generated card keys");
        //        return result;
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger?.LogError(ex, "Error during key generation");
        //        throw new KeyGenerationException("Key generation failed", ex);
        //    }
        //}

        private QSparcKeyGenerator.KeyGenerationResult GenerateCardKeys(CardInput cardInput)
        {
            try
            {
                _logger?.LogInformation($"Generating card keys with key length: {cardInput.IccKeyLength} bits");

                // Check if using existing key
                if (cardInput.UseExistingIccKey && cardInput.ExistingIccKey != null)
                {
                    _logger?.LogInformation("Using existing ICC RSA key");

                    // Validate existing key
                    if (cardInput.ExistingIccKey.Modulus.Length != 96)  // 768 bits
                    {
                        throw new ArgumentException(
                            $"Invalid existing ICC key length: {cardInput.ExistingIccKey.Modulus.Length * 8} bits. " +
                            "Expected 768 bits.");
                    }

                    // Return existing key in result format
                    return new QSparcKeyGenerator.KeyGenerationResult
                    {
                        Success = true,
                        IccRsaKey = cardInput.ExistingIccKey,
                        // Master keys can still be used directly
                        MasterKeys = DeriveKeySet(cardInput)
                    };
                }

                // Generate new keys if not using existing
                _logger?.LogInformation("Generating new ICC RSA key pair");
                return QSparcKeyGenerator.GenerateQSparcKeys(
                    cardInput.MasterKeys.ImkAc,
                    cardInput.MasterKeys.ImkSmi,
                    cardInput.MasterKeys.ImkSmc,
                    null,  // No derivation data for now
                    cardInput.DerivationOption,
                    cardInput.IccKeyLength);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to generate/load ICC keys");
                return new QSparcKeyGenerator.KeyGenerationResult
                {
                    Success = false,
                    ErrorMessage = $"Key generation failed: {ex.Message}"
                };
            }
        }

        private MasterKeySet DeriveKeySet(CardInput cardInput)
        {
            return new MasterKeySet
            {
                Ac = EmvRsaHelper.HexStringToByteArray(cardInput.MasterKeys.ImkAc),
                Smi = EmvRsaHelper.HexStringToByteArray(cardInput.MasterKeys.ImkSmi),
                Smc = EmvRsaHelper.HexStringToByteArray(cardInput.MasterKeys.ImkSmc)
            };
        }

        private byte[] ApplyPkcs1Padding(byte[] data, int blockSize)
        {
            // EMV-specific padding
            var padded = new byte[blockSize];
            padded[0] = 0x00;
            padded[1] = 0x01; // For signing

            // Fill with 0xFF
            for (int i = 2; i < blockSize - data.Length - 1; i++)
            {
                padded[i] = 0xFF;
            }

            // Add separator
            padded[blockSize - data.Length - 1] = 0x00;

            // Copy data
            Buffer.BlockCopy(data, 0, padded, blockSize - data.Length, data.Length);

            return padded;
        }

        private byte[] CalculatePrivateExponent(byte[] modulus, byte[] publicExponent)
        {
            // For testing/development only!
            // In production, private keys should come from secure storage
            try
            {
                using (var rsa = new RSACryptoServiceProvider())
                {
                    // Generate a new key pair
                    rsa.ImportParameters(new RSAParameters
                    {
                        Modulus = modulus,
                        Exponent = publicExponent
                    });

                    // Export the full parameters including private components
                    var fullParams = rsa.ExportParameters(true);
                    return fullParams.D;
                }
            }
            catch
            {
                // If calculation fails, use a dummy private exponent (FOR TESTING ONLY!)
                return new byte[] { 0x03 };
            }
        }

        public class EmvPreparationResult
        {
            public bool Success { get; set; }
            public string ErrorMessage { get; set; }
            public byte[] IssuerCertificate { get; set; }  // Tag 90
            public byte[] IccCertificate { get; set; }     // Tag 9F46
            public byte[] SignedStaticData { get; set; }   // Tag 93
            public QSparcKeyGenerator.RsaKeyParameters IccRsaKey { get; set; }
            public MasterKeySet DerivedKeys { get; set; }
        }

        public async Task<EmvPreparationResult> PrepareCardData(
    CardInput cardInput,
    CertificateInput certInput)
        {
            try
            {
                _logger?.LogInformation("Starting EMV data preparation");
                _logger?.LogInformation($"Processing PAN: {cardInput.Pan}, PSN: {cardInput.Psn}");

                // 1. Load all keys from XML files
                _logger?.LogInformation("Loading keys from XML files");
                var keys = _keyLoader.LoadKeys(
                    certInput.CaKeyFile,
                    certInput.IssuerKeyFile,
                    certInput.UseExistingIccKey ? certInput.IccKeyFile : null);

                // 2. Validate inputs
                //ValidateInputParameters(cardInput, certInput);

                // 3. Generate/Get ICC Keys
                _logger?.LogInformation($"Generating ICC keys with length: {cardInput.IccKeyLength} bits");
                var keyGenResult = GenerateCardKeys(cardInput);
                if (!keyGenResult.Success)
                {
                    _logger?.LogError($"Key generation failed: {keyGenResult.ErrorMessage}");
                    return new EmvPreparationResult
                    {
                        Success = false,
                        ErrorMessage = keyGenResult.ErrorMessage
                    };
                }
                _logger?.LogInformation("ICC key generation successful");
                LogKeyDetails(keyGenResult);

                // 4. Generate Issuer Certificate using CA key from XML
                _logger?.LogInformation("Generating Issuer Certificate");
                byte[] issuerCert;
                try
                {
                    issuerCert = await GenerateIssuerCertificateRsa(
                        BitConverter.ToString(keys.CaKey.Modulus).Replace("-", ""),     // CA Modulus from XML
                        BitConverter.ToString(keys.CaKey.Exponent).Replace("-", ""),    // CA Exponent from XML
                        BitConverter.ToString(keys.IssuerKey.Modulus).Replace("-", ""), // Issuer Modulus from XML
                        BitConverter.ToString(keys.IssuerKey.Exponent).Replace("-", ""),// Issuer Exponent from XML
                        cardInput.Pan.Substring(0, 6),
                        certInput.IssuerExpiryDate);

                    _logger?.LogInformation($"Issuer Certificate generated, length: {issuerCert.Length} bytes");
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Issuer Certificate generation failed");
                    throw new CertificateGenerationException("Failed to generate Issuer Certificate", ex);
                }

                // 5. Format Static Application Data
                string staticData = FormatStaticApplicationData(cardInput);
                _logger?.LogInformation($"Static Application Data: {staticData}");
                byte[] staticDataBytes = EmvRsaHelper.HexStringToByteArray(staticData);

                // 6. Sign Static Data using Issuer key from XML
                _logger?.LogInformation("Signing Static Application Data");
                byte[] signedStaticData;
                try
                {
                    var signer = new EmvStaticDataSigner();
                    var signResult = signer.SignStaticApplicationData(
                        staticDataBytes,
                        BitConverter.ToString(keys.IssuerKey.Modulus).Replace("-", ""),
                        BitConverter.ToString(keys.IssuerKey.D).Replace("-", ""));  // Use private key from XML

                    if (!signResult.Success)
                    {
                        throw new CertificateGenerationException(
                            $"Static data signing failed: {signResult.ErrorMessage}");
                    }

                    signedStaticData = signResult.SignedData;
                    _logger?.LogInformation($"Static data signed successfully, length: {signedStaticData.Length} bytes");
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Static data signing failed");
                    throw;
                }

                // 7. Generate ICC Certificate using Issuer key from XML
                _logger?.LogInformation("Generating ICC Certificate");
                byte[] iccCert;
                try
                {
                    iccCert = await GenerateIccCertificateRsa(
                        BitConverter.ToString(keys.IssuerKey.Modulus).Replace("-", ""),
                        BitConverter.ToString(keys.IssuerKey.D).Replace("-", ""),  // Use private key from XML
                        cardInput.Pan,
                        staticData,
                        keyGenResult.IccRsaKey.Modulus,
                        keyGenResult.IccRsaKey.PublicExponent,
                        certInput.IccExpiryDate);
                    _logger?.LogInformation($"ICC Certificate generated, length: {iccCert.Length} bytes");
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "ICC Certificate generation failed");
                    throw;
                }

                // 8. Return all results
                var result = new EmvPreparationResult
                {
                    Success = true,
                    IssuerCertificate = issuerCert,
                    IccCertificate = iccCert,
                    SignedStaticData = signedStaticData,
                    IccRsaKey = keyGenResult.IccRsaKey,
                    DerivedKeys = keyGenResult.MasterKeys
                };

                LogResultDetails(result);
                _logger?.LogInformation("EMV data preparation completed successfully");
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "EMV data preparation failed");
                return new EmvPreparationResult
                {
                    Success = false,
                    ErrorMessage = $"EMV data preparation failed: {ex.Message}"
                };
            }
        }


        //        // 3. Generate ICC Certificate
        //        _logger?.LogInformation("Generating ICC Certificate");
        //        byte[] iccCert;
        //        try
        //        {
        //            string staticData = FormatStaticApplicationData(cardInput);
        //            _logger?.LogInformation($"Static Application Data: {staticData}");

        //            // Use IssuerPrivateKey.PrivateExponent instead of IssuerPrivateExponent
        //            iccCert = await GenerateIccCertificateRsa(
        //                certInput.IssuerModulus,
        //                certInput.IssuerPrivateKey.PrivateExponent,  // Changed this line
        //                cardInput.Pan,
        //                staticData,
        //                keyGenResult.IccRsaKey.Modulus,
        //                keyGenResult.IccRsaKey.PublicExponent,
        //                certInput.IccExpiryDate,
        //                !cardInput.UseExistingIccKey);

        //            _logger?.LogInformation($"ICC Certificate generated, length: {iccCert.Length} bytes");
        //        }
        //        catch (Exception ex)
        //        {
        //            _logger?.LogError(ex, "ICC Certificate generation failed");
        //            throw new CertificateGenerationException("Failed to generate ICC Certificate", ex);
        //        }

        //        // 4. Create and return result
        //        var result = new EmvPreparationResult
        //        {
        //            Success = true,
        //            IssuerCertificate = issuerCert,
        //            IccCertificate = iccCert,
        //            IccRsaKey = keyGenResult.IccRsaKey,
        //            DerivedKeys = keyGenResult.MasterKeys
        //        };

        //        LogResultDetails(result);
        //        _logger?.LogInformation("EMV data preparation completed successfully");

        //        return result;
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger?.LogError(ex, "EMV data preparation failed");
        //        return new EmvPreparationResult
        //        {
        //            Success = false,
        //            ErrorMessage = $"EMV data preparation failed: {ex.Message}"
        //        };
        //    }
        //}

        //private async Task<byte[]> GenerateIssuerCertificateRsa(
        //string caModulus,
        //string caExponent,
        //string issuerModulus,
        //string issuerExponent,
        //string issuerIdentifier,
        //string expiryDate)
        //{
        //    try
        //    {
        //        _logger?.LogInformation("Building Issuer Certificate");

        //        // Build certificate data using the builder
        //        byte[] certificateData = IssuerCertificateBuilder.BuildIssuerCertificate(
        //            issuerIdentifier,
        //            expiryDate,
        //            issuerModulus,
        //            issuerExponent);

        //        _logger?.LogInformation($"Certificate data built, length: {certificateData.Length} bytes");

        //        // Create RSA parameters and encrypt
        //        var caKey = EmvRsaHelper.CreateRsaParameters(caModulus, caExponent);
        //        return EmvRsaHelper.EncryptIssuerCertificate(certificateData, caKey);
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger?.LogError(ex, "Failed to generate Issuer Certificate");
        //        throw new CertificateGenerationException(
        //            $"Failed to generate Issuer Certificate: {ex.Message}", ex);
        //    }
        //}

        private async Task<byte[]> GenerateIssuerCertificateRsa(
    string caModulus,
    string caExponent,
    string issuerModulus,
    string issuerExponent,
    string issuerIdentifier,
    string expiryDate)
        {
            try
            {
                _logger?.LogInformation("Building and signing Issuer Certificate");

                var caKey = new RSAParameters
                {
                    Modulus = EmvRsaHelper.HexStringToByteArray(caModulus),
                    Exponent = EmvRsaHelper.HexStringToByteArray(caExponent)
                };

                return EmvCertificateBuilder.BuildAndSignIssuerCertificate(
                    issuerIdentifier,
                    expiryDate,
                    issuerModulus,
                    issuerExponent,
                    caKey);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to generate Issuer Certificate");
                throw;
            }
        }

        //private async Task<byte[]> GenerateIccCertificateRsa(
        // string issuerModulus,
        // string issuerPrivateExponent,
        // string pan,
        // string staticData,
        // byte[] iccModulus,
        // byte[] iccExponent,
        // string expiryDate)
        //{
        //    try
        //    {
        //        _logger?.LogInformation("Building ICC Certificate");

        //        // Create certificate builder
        //        var builder = new CertificateBuilder();

        //        // Add certificate header
        //        builder.AddByte(0x6A);  // Header
        //        builder.AddByte(0x04);  // ICC Certificate Format

        //        // Add PAN (padded to 10 bytes)
        //        string paddedPan = pan.PadRight(20, 'F');
        //        builder.AddHexString(paddedPan);

        //        // Add expiry date
        //        builder.AddHexString(expiryDate);

        //        // Add serial number (typically "000002" for ICC)
        //        builder.AddHexString("000002");

        //        // Add algorithm indicators
        //        builder.AddByte(0x01);  // Hash Algorithm (SHA-1)
        //        builder.AddByte(0x01);  // ICC Public Key Algorithm (RSA)

        //        // Add ICC Public Key Length
        //        builder.AddByte((byte)iccModulus.Length);

        //        // Add ICC Public Key Exponent Length
        //        builder.AddByte((byte)iccExponent.Length);

        //        // Add ICC Public Key
        //        builder.AddBytes(iccModulus);

        //        // Add Static Application Data
        //        if (!string.IsNullOrEmpty(staticData))
        //        {
        //            builder.AddHexString(staticData);
        //        }

        //        // Calculate and add hash
        //        byte[] dataToHash = builder.GetCertificateData();
        //        using (var sha1 = SHA1.Create())
        //        {
        //            byte[] hash = sha1.ComputeHash(dataToHash);
        //            builder.AddBytes(hash);
        //        }

        //        // Add trailer
        //        builder.AddByte(0xBC);

        //        // Get complete certificate data
        //        byte[] certificateData = builder.GetCertificateData();
        //        _logger?.LogInformation($"ICC Certificate data built, length: {certificateData.Length} bytes");

        //        // Create RSA parameters and encrypt
        //        var issuerKey = new RSAParameters
        //        {
        //            Modulus = EmvRsaHelper.HexStringToByteArray(issuerModulus),
        //            D = EmvRsaHelper.HexStringToByteArray(issuerPrivateExponent)
        //        };

        //        return EmvRsaHelper.EncryptIccCertificate(certificateData, issuerKey);
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger?.LogError(ex, "Failed to generate ICC Certificate");
        //        throw new CertificateGenerationException(
        //            $"Failed to generate ICC Certificate: {ex.Message}", ex);
        //    }
        //}

        private async Task<byte[]> GenerateIccCertificateRsa(
        string issuerModulus,
        string issuerPrivateExponent,
        string pan,
        string staticData,
        byte[] iccModulus,
        byte[] iccExponent,
        string expiryDate,
        bool isManualExponent = false)
        {
            try
            {
                _logger?.LogInformation("Building ICC Certificate");

                // Enhanced input validation
                //ValidateIccCertificateInputs(
                //    issuerModulus,
                //    issuerPrivateExponent,
                //    iccModulus,
                //    iccExponent,
                //    pan,
                //    expiryDate);
                //// Enhanced validation
                //if (string.IsNullOrEmpty(issuerPrivateExponent))
                //{
                //    _logger?.LogError("Issuer private exponent is missing");
                //    throw new ArgumentException("Issuer private exponent is required");
                //}
                _logger?.LogInformation($"Using issuer modulus length: {issuerModulus.Length / 2} bytes");
                _logger?.LogInformation($"Using issuer private exponent length: {issuerPrivateExponent.Length / 2} bytes");

                // Format complete static data with all required EMV tags
                string completeStaticData = FormatFullStaticApplicationData(pan);
                _logger?.LogInformation($"Complete Static Data: {completeStaticData}");

                // Build certificate
                using (var ms = new MemoryStream())
                using (var writer = new BinaryWriter(ms))
                {
                    // 1. Header (6A 04)
                    writer.Write((byte)0x6A);
                    writer.Write((byte)0x04);  // ICC Certificate Format

                    // 2. PAN (20 bytes, padded with 'F')
                    string paddedPan = pan.PadRight(20, 'F');
                    _logger?.LogDebug($"Padded PAN: {paddedPan}");
                    writer.Write(EmvRsaHelper.HexStringToByteArray(paddedPan));

                    // 3. Expiry Date (YYMM)
                    writer.Write(EmvRsaHelper.HexStringToByteArray(expiryDate));

                    // 4. Certificate Serial Number (000002 for ICC)
                    writer.Write(EmvRsaHelper.HexStringToByteArray("000002"));

                    // 5. Hash and PKI Algorithm Indicators
                    writer.Write((byte)0x01);  // SHA-1
                    writer.Write((byte)0x01);  // RSA

                    // 6. ICC Public Key Length (96 bytes = 0x60)
                    writer.Write((byte)0x60);

                    // 7. ICC Public Key Exponent Length
                    writer.Write((byte)0x01);

                    // 8. ICC Public Key Modulus
                    writer.Write(iccModulus);
                    _logger?.LogDebug($"ICC Modulus Length: {iccModulus.Length}");

                    // 9. Padding (BB)
                    int paddingLength = 42;  // 176 - 96 - headers - hash
                    for (int i = 0; i < paddingLength; i++)
                    {
                        writer.Write((byte)0xBB);
                    }

                    // 10. Static Application Data
                    byte[] staticDataBytes = EmvRsaHelper.HexStringToByteArray(completeStaticData);
                    writer.Write(staticDataBytes);
                    _logger?.LogDebug($"Static Data Length: {staticDataBytes.Length}");

                    // Get data for hash calculation
                    byte[] dataToHash = ms.ToArray();
                    _logger?.LogDebug($"Data to hash length: {dataToHash.Length}");

                    // 11. Calculate and add hash
                    using (var sha1 = SHA1.Create())
                    {
                        byte[] hash = sha1.ComputeHash(dataToHash);
                        writer.Write(hash);
                        _logger?.LogDebug($"Hash: {BitConverter.ToString(hash)}");
                    }

                    // 12. Trailer
                    writer.Write((byte)0xBC);

                    // Get complete certificate data
                    byte[] certificateData = ms.ToArray();


                    _logger?.LogInformation($"ICC Certificate built, length: {certificateData.Length} bytes");

                    // Create complete RSA parameters for signing
                    var issuerKey = new RSAParameters
                    {
                        Modulus = EmvRsaHelper.HexStringToByteArray(issuerModulus),
                        Exponent = new byte[] { 0x03 },
                        D = EmvRsaHelper.HexStringToByteArray(issuerPrivateExponent)
                    };

                    _logger?.LogInformation("Signing ICC Certificate with Issuer key");
                    _logger?.LogInformation($"Issuer key modulus length: {issuerKey.Modulus.Length}");
                    _logger?.LogInformation($"Issuer key private exponent length: {issuerKey.D.Length}");

                    // Sign certificate
                    _logger?.LogInformation("Signing ICC Certificate with Issuer key");
                    return EmvRsaHelper.FormatAndSignCertificate(certificateData, issuerKey);
                    //        return EmvRsaHelper.SignCertificateData(
                    //certificateData,
                    //issuerKey,
                    //isManualExponent);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to generate ICC Certificate");
                throw new CertificateGenerationException(
                    $"Failed to generate ICC Certificate: {ex.Message}", ex);
            }
        }

        private string FormatFullStaticApplicationData(string pan)
        {
            var sb = new StringBuilder();

            // Add mandatory EMV tags as per your example
            sb.AppendFormat("5A0A{0}", pan);                    // PAN
            sb.AppendFormat("5F340101");                        // PSN = 01
            sb.AppendFormat("5713{0}", pan);                    // Track 2 Equivalent Data
            sb.Append("D2804620763000000F");                    // ICC Dynamic Data
            sb.Append("9F080200025F24032804305F25032305019F0702FFC0"); // Application Version, Expiry, Effect. dates, etc
            sb.Append("5F280207848E0E000000000000000042035E031F03");   // Issuer Country Code, CVM List
            sb.Append("DF34060000000000009F0D05B4F8DC9800");          // Default DDOL
            sb.Append("9F0E0500000000009F0F05B4F8DC9800");           // IAC values
            sb.Append("8C219F02069F03069F1A0295055F2A029A039C019F37049F35019F34039F21039F1C08"); // CDOL1
            sb.Append("8D1891108A0295059F37049F4C08DF1502DF2208DF2308DF45609F4A01821900");      // CDOL2

            return sb.ToString();
        }

        //   private void ValidateIccCertificateInputs(
        //string issuerModulus,
        //string issuerPrivateExponent,
        //byte[] iccModulus,
        //byte[] iccExponent,
        //string pan,
        //string expiryDate)
        //   {
        //       if (string.IsNullOrEmpty(issuerModulus))
        //           throw new ArgumentException("Issuer modulus is required");

        //       if (string.IsNullOrEmpty(issuerPrivateExponent))
        //       {
        //           _logger?.LogError("Issuer private exponent is missing");
        //           throw new ArgumentException("Issuer private exponent is required");
        //       }

        //       if (iccModulus == null || iccModulus.Length != 96)
        //           throw new ArgumentException($"Invalid ICC modulus length: {iccModulus?.Length ?? 0} (expected 96)");

        //       if (iccExponent == null || iccExponent.Length != 1 || iccExponent[0] != 0x03)
        //           throw new ArgumentException("ICC exponent must be 03");

        //       if (string.IsNullOrEmpty(pan) || pan.Length < 8)
        //           throw new ArgumentException("Invalid PAN length");

        //       if (string.IsNullOrEmpty(expiryDate) || expiryDate.Length != 4)
        //           throw new ArgumentException("Invalid expiry date format (must be YYMM)");

        //       _logger?.LogInformation($"Using issuer private key length: {issuerPrivateExponent.Length / 2} bytes");
        //   }
        private void ValidateIccCertificateInputs(
       string issuerModulus,
       string issuerPrivateExponent,
       byte[] iccModulus,
       byte[] iccExponent,
       string pan,
       string expiryDate,
       bool isLoadedKey = false)  // Add parameter to indicate if using loaded key
        {
            try
            {
                _logger?.LogInformation("Validating ICC Certificate inputs");
                _logger?.LogInformation($"Using loaded key: {isLoadedKey}");

                // Validate Issuer components
                if (string.IsNullOrEmpty(issuerModulus))
                {
                    _logger?.LogError("Issuer modulus is missing");
                    throw new ArgumentException("Issuer modulus is required");
                }

                // Validate private exponent based on key source and if isloadedkey is false throw error if loadedkey is true then its valid
                if (string.IsNullOrEmpty(issuerPrivateExponent))
                    {
                    if (!isLoadedKey)
                    {
                        _logger?.LogError("Issuer private exponent is missing (required for manual key)");
                        throw new ArgumentException("Issuer private exponent is required for manual key input");
                    }
                }
                //if (!isLoadedKey && string.IsNullOrEmpty(issuerPrivateExponent))
                //{
                //    _logger?.LogError("Issuer private exponent is missing (required for manual key)");
                //    throw new ArgumentException("Issuer private exponent is required for manual key input");
                //}

                // Log key component lengths
                _logger?.LogInformation($"Issuer modulus length: {issuerModulus.Length / 2} bytes");
                if (!string.IsNullOrEmpty(issuerPrivateExponent))
                {
                    _logger?.LogInformation($"Issuer private exponent length: {issuerPrivateExponent.Length / 2} bytes");
                }

                // Validate ICC components
                if (iccModulus == null)
                {
                    _logger?.LogError("ICC modulus is null");
                    throw new ArgumentException("ICC modulus is required");
                }

                if (iccModulus.Length != 96)  // 768 bits
                {
                    _logger?.LogError($"Invalid ICC modulus length: {iccModulus.Length} bytes");
                    throw new ArgumentException($"Invalid ICC modulus length: {iccModulus.Length} bytes (expected 96)");
                }

                // Validate ICC exponent
                if (iccExponent == null)
                {
                    _logger?.LogError("ICC exponent is null");
                    throw new ArgumentException("ICC exponent is required");
                }

                if (iccExponent.Length != 1 || iccExponent[0] != 0x03)
                {
                    _logger?.LogError($"Invalid ICC exponent: {BitConverter.ToString(iccExponent)}");
                    throw new ArgumentException("ICC exponent must be 03");
                }

                // Validate PAN
                if (string.IsNullOrEmpty(pan))
                {
                    _logger?.LogError("PAN is missing");
                    throw new ArgumentException("PAN is required");
                }

                if (pan.Length < 8)
                {
                    _logger?.LogError($"Invalid PAN length: {pan.Length}");
                    throw new ArgumentException("PAN must be at least 8 digits");
                }

                // Validate expiry date
                if (string.IsNullOrEmpty(expiryDate))
                {
                    _logger?.LogError("Expiry date is missing");
                    throw new ArgumentException("Expiry date is required");
                }

                if (expiryDate.Length != 4)
                {
                    _logger?.LogError($"Invalid expiry date format: {expiryDate}");
                    throw new ArgumentException("Invalid expiry date format (must be YYMM)");
                }

                // Additional EMV-specific validations
                ValidateEmvKeyLengths(issuerModulus, iccModulus);

                _logger?.LogInformation("ICC Certificate inputs validated successfully");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "ICC Certificate input validation failed");
                throw;
            }
        }

        private void ValidateEmvKeyLengths(string issuerModulus, byte[] iccModulus)
        {
            // Issuer key should be 1408 bits (176 bytes)
            int issuerModulusBytes = issuerModulus.Length / 2;
            if (issuerModulusBytes != 176)
            {
                _logger?.LogError($"Invalid issuer key length: {issuerModulusBytes} bytes");
                throw new ArgumentException($"Invalid issuer key length: {issuerModulusBytes} bytes (expected 176)");
            }

            // ICC key should be 768 bits (96 bytes)
            if (iccModulus.Length != 96)
            {
                _logger?.LogError($"Invalid ICC key length: {iccModulus.Length} bytes");
                throw new ArgumentException($"Invalid ICC key length: {iccModulus.Length} bytes (expected 96)");
            }
        }

        private void ValidateInputParameters(CardInput cardInput, CertificateInput certInput)
        {
            _logger?.LogInformation("Validating input parameters");

            // Card Input Validation
            if (cardInput == null)
                throw new ArgumentNullException(nameof(cardInput));

            if (string.IsNullOrEmpty(cardInput.Pan))
                throw new ArgumentException("PAN is required");

            if (cardInput.Pan.Length < 8)
                throw new ArgumentException("PAN must be at least 8 digits");

            if (string.IsNullOrEmpty(cardInput.Psn))
                throw new ArgumentException("PSN is required");

            // Certificate Input Validation
            if (certInput == null)
                throw new ArgumentNullException(nameof(certInput));

            // CA Key Validation
            if (string.IsNullOrEmpty(certInput.CaModulus))
                throw new ArgumentException("CA Modulus is required");

            var caModulusLength = certInput.CaModulus.Length / 2; // Convert hex string length to bytes
            if (caModulusLength != 248) // 1984 bits
                throw new ArgumentException($"Invalid CA Modulus length. Expected 248 bytes, got {caModulusLength}");

            if (string.IsNullOrEmpty(certInput.CaExponent))
                throw new ArgumentException("CA Exponent is required");

            // Issuer Key Validation
            if (string.IsNullOrEmpty(certInput.IssuerModulus))
                throw new ArgumentException("Issuer Modulus is required");

            var issuerModulusLength = certInput.IssuerModulus.Length / 2;
            if (issuerModulusLength != 176) // 1408 bits
                throw new ArgumentException($"Invalid Issuer Modulus length. Expected 176 bytes, got {issuerModulusLength}");

            //// Expiry Date Validation
            //if (!IsValidExpiryDate(certInput.IssuerExpiryDate))
            //    throw new ArgumentException("Invalid Issuer expiry date format (YYMM required)");

            //if (!IsValidExpiryDate(certInput.IccExpiryDate))
            //    throw new ArgumentException("Invalid ICC expiry date format (YYMM required)");

            _logger?.LogInformation("Input parameters validated successfully");
        }

        private void LogKeyDetails(QSparcKeyGenerator.KeyGenerationResult keyGenResult)
        {
            _logger?.LogInformation($"ICC Public Key Modulus Length: {keyGenResult.IccRsaKey.Modulus.Length} bytes");
            _logger?.LogInformation($"ICC Public Key Exponent: {BitConverter.ToString(keyGenResult.IccRsaKey.PublicExponent)}");

            _logger?.LogInformation("Master Keys were derived successfully");
        }

        private void LogResultDetails(EmvPreparationResult result)
        {
            _logger?.LogInformation($"Issuer Certificate Length: {result.IssuerCertificate.Length} bytes");
            _logger?.LogInformation($"ICC Certificate Length: {result.IccCertificate.Length} bytes");
            _logger?.LogInformation($"ICC Public Key Length: {result.IccRsaKey.Modulus.Length} bytes");
        }

        
       
        private bool IsValidExpiryDate(string date)        
        {
            if (string.IsNullOrEmpty(date) || date.Length != 4)
                return false;

            if (!int.TryParse(date.Substring(0, 2), out int year))
                return false;

            if (!int.TryParse(date.Substring(2, 2), out int month))
                return false;

            return month >= 1 && month <= 12;
        }

        private string FormatStaticApplicationData(CardInput cardInput)
        {
            var sb = new StringBuilder();

            // Add PAN
            sb.AppendFormat("5A{0:X2}{1}", cardInput.Pan.Length, cardInput.Pan);

            // Add PSN
            sb.AppendFormat("5F34{0:X2}{1}", 1, cardInput.Psn);

            // Add Expiry
            if (!string.IsNullOrEmpty(cardInput.ExpiryDate))
                sb.AppendFormat("5F24{0:X2}{1}", 3, cardInput.ExpiryDate);

            return sb.ToString();
        }

        private void ValidateMasterKeys(MasterKeysInput masterKeys)
        {
            // Validate IMK-AC
            if (string.IsNullOrEmpty(masterKeys.ImkAc))
                throw new ArgumentException("IMK-AC is required");

            if (!IsValidHexString(masterKeys.ImkAc))
                throw new ArgumentException("IMK-AC must be a valid hexadecimal string");

            // Validate IMK-SMI
            if (string.IsNullOrEmpty(masterKeys.ImkSmi))
                throw new ArgumentException("IMK-SMI is required");

            if (!IsValidHexString(masterKeys.ImkSmi))
                throw new ArgumentException("IMK-SMI must be a valid hexadecimal string");

            // Validate IMK-SMC
            if (string.IsNullOrEmpty(masterKeys.ImkSmc))
                throw new ArgumentException("IMK-SMC is required");

            if (!IsValidHexString(masterKeys.ImkSmc))
                throw new ArgumentException("IMK-SMC must be a valid hexadecimal string");

            // Validate key lengths
            if (masterKeys.ImkAc.Length != 32 && masterKeys.ImkAc.Length != 48 && masterKeys.ImkAc.Length != 64)
                throw new ArgumentException("IMK-AC must be 16, 24, or 32 bytes (32, 48, or 64 hex characters)");

            if (masterKeys.ImkSmi.Length != 32 && masterKeys.ImkSmi.Length != 48 && masterKeys.ImkSmi.Length != 64)
                throw new ArgumentException("IMK-SMI must be 16, 24, or 32 bytes (32, 48, or 64 hex characters)");

            if (masterKeys.ImkSmc.Length != 32 && masterKeys.ImkSmc.Length != 48 && masterKeys.ImkSmc.Length != 64)
                throw new ArgumentException("IMK-SMC must be 16, 24, or 32 bytes (32, 48, or 64 hex characters)");
        }

        private void ValidateGeneratedKeys(QSparcKeyGenerator.KeyGenerationResult result)
        {
            // Validate RSA key components
            if (result.IccRsaKey == null)
                throw new KeyGenerationException("ICC RSA key generation failed");

            if (result.IccRsaKey.Modulus == null || result.IccRsaKey.Modulus.Length == 0)
                throw new KeyGenerationException("Invalid ICC RSA modulus");

            if (result.IccRsaKey.PublicExponent == null || result.IccRsaKey.PublicExponent.Length == 0)
                throw new KeyGenerationException("Invalid ICC RSA public exponent");

            // Validate master keys if derived
            if (result.MasterKeys != null)
            {
                if (result.MasterKeys.Ac == null || result.MasterKeys.Ac.Length == 0)
                    throw new KeyGenerationException("Invalid derived AC master key");

                if (result.MasterKeys.Smi == null || result.MasterKeys.Smi.Length == 0)
                    throw new KeyGenerationException("Invalid derived SMI master key");

                if (result.MasterKeys.Smc == null || result.MasterKeys.Smc.Length == 0)
                    throw new KeyGenerationException("Invalid derived SMC master key");
            }
        }

        private bool IsValidHexString(string hex)
        {
            if (string.IsNullOrEmpty(hex))
                return false;

            // Remove any spaces or hyphens
            hex = hex.Replace(" ", "").Replace("-", "");

            // Check if length is even
            if (hex.Length % 2 != 0)
                return false;

            // Check if all characters are valid hex
            return System.Text.RegularExpressions.Regex.IsMatch(
                hex,
                @"^[0-9A-Fa-f]+$");
        }
    }

    public class KeyGenerationException : Exception
    {
        public KeyGenerationException(string message) : base(message) { }
        public KeyGenerationException(string message, Exception innerException)
            : base(message, innerException) { }
    }

    public class EmvCertificateResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public byte[] IssuerCertificate { get; set; }      // Tag 90
        public byte[] IccCertificate { get; set; }         // Tag 9F46
        public QSparcKeyGenerator.RsaKeyParameters IccRsaKey { get; set; }
        public MasterKeySet DerivedKeys { get; set; }
        public Dictionary<string, byte[]> AdditionalTags { get; set; }

        public EmvCertificateResult()
        {
            AdditionalTags = new Dictionary<string, byte[]>();
        }

        public void AddTag(string tag, byte[] value)
        {
            AdditionalTags[tag] = value;
        }

        public byte[] GetFormattedTlvData()
        {
            var ms = new MemoryStream();

            // Add Issuer Certificate
            if (IssuerCertificate != null)
            {
                WriteTlv(ms, "90", IssuerCertificate);
            }

            // Add ICC Certificate
            if (IccCertificate != null)
            {
                WriteTlv(ms, "9F46", IccCertificate);
            }

            // Add ICC Public Key Components
            if (IccRsaKey != null)
            {
                WriteTlv(ms, "9F47", IccRsaKey.PublicExponent);  // ICC Public Key Exponent
                WriteTlv(ms, "9F48", IccRsaKey.Modulus);         // ICC Public Key Remainder
            }

            // Add any additional tags
            foreach (var tag in AdditionalTags)
            {
                WriteTlv(ms, tag.Key, tag.Value);
            }

            return ms.ToArray();
        }

        private void WriteTlv(MemoryStream ms, string tag, byte[] value)
        {
            var tagBytes = HexStringToByteArray(tag);
            ms.Write(tagBytes, 0, tagBytes.Length);

            // Write length
            if (value.Length < 128)
            {
                ms.WriteByte((byte)value.Length);
            }
            else
            {
                // Handle longer lengths according to BER-TLV rules
                byte[] lengthBytes = BitConverter.GetBytes(value.Length);
                ms.WriteByte((byte)(0x80 | lengthBytes.Length));
                ms.Write(lengthBytes, 0, lengthBytes.Length);
            }

            // Write value
            ms.Write(value, 0, value.Length);
        }

        public static byte[] HexStringToByteArray(string hex)
        {
            if (string.IsNullOrEmpty(hex))
                throw new ArgumentException("Hex string cannot be null or empty");

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

    public class CertificateGenerationException : Exception
    {
        public CertificateGenerationException(string message) : base(message) { }
        public CertificateGenerationException(string message, Exception inner) : base(message, inner) { }
    }
}
