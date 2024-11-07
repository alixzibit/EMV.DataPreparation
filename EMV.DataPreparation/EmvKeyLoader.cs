using EMV.DataPreparation;
using Microsoft.Extensions.Logging;
using Multos.Crypto.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

public class EmvKeyLoader
{
    private readonly ILogger<EmvKeyLoader> _logger;

    public class EmvKeySet
    {
        public RSAParameters CaKey { get; set; }
        public RSAParameters IssuerKey { get; set; }
        public QSparcKeyGenerator.RsaKeyParameters IccKey { get; set; }  // Optional, for loaded ICC keys
    }

    public RSAParameters LoadCaKey(string caKeyFile)
    {
        var caKey = LoadRsaKeyFromXml(caKeyFile);
        ValidateCaKey(caKey);
        return caKey;
    }

    public RSAParameters LoadIssuerKey(string issuerKeyFile)
    {
        var issuerKey = LoadRsaKeyFromXml(issuerKeyFile);
        ValidateIssuerKey(issuerKey);
        return issuerKey;
    }

    public EmvKeySet LoadKeys(string caKeyFile, string issuerKeyFile, string iccKeyFile = null)
    {
        try
        {
            _logger?.LogInformation("Loading EMV keys from XML files");

            // Load CA key
            _logger?.LogInformation("Loading CA key");
            var caKey = LoadRsaKeyFromXml(caKeyFile);
            ValidateCaKey(caKey);

            // Load Issuer key
            _logger?.LogInformation("Loading Issuer key");
            var issuerKey = LoadRsaKeyFromXml(issuerKeyFile);
            ValidateIssuerKey(issuerKey);

            // Create result
            var keySet = new EmvKeySet
            {
                CaKey = caKey,
                IssuerKey = issuerKey
            };

            // Optionally load ICC key if provided
            if (!string.IsNullOrEmpty(iccKeyFile))
            {
                _logger?.LogInformation("Loading ICC key");
                keySet.IccKey = LoadIccKeyFromXml(iccKeyFile);
            }

            _logger?.LogInformation("All keys loaded successfully");
            return keySet;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load EMV keys");
            throw;
        }
    }

    private RSAParameters LoadRsaKeyFromXml(string xmlFile)
    {
        var doc = XDocument.Load(xmlFile);
        var root = doc.Root;

        if (root.Name != "RSAKeyPair")
            throw new ArgumentException("Invalid XML format: Root element must be 'RSAKeyPair'");

        return new RSAParameters
        {
            Modulus = GetComponentValue(root, "Modulus"),
            Exponent = GetComponentValue(root, "Exponent"),
            D = GetComponentValue(root, "D"),
            P = GetComponentValue(root, "P"),
            Q = GetComponentValue(root, "Q"),
            DP = GetComponentValue(root, "DP"),
            DQ = GetComponentValue(root, "DQ"),
            InverseQ = GetComponentValue(root, "InverseQ")
        };
    }

    private QSparcKeyGenerator.RsaKeyParameters LoadIccKeyFromXml(string xmlFile)
    {
        var rsaParams = LoadRsaKeyFromXml(xmlFile);
        return new QSparcKeyGenerator.RsaKeyParameters
        {
            Modulus = rsaParams.Modulus,
            PublicExponent = rsaParams.Exponent,
            PrivateExponent = rsaParams.D,
            P = rsaParams.P,
            Q = rsaParams.Q,
            DP = rsaParams.DP,
            DQ = rsaParams.DQ,
            InverseQ = rsaParams.InverseQ
        };
    }

    private byte[] GetComponentValue(XElement root, string elementName)
    {
        var element = root.Element(elementName);
        if (element == null)
            return null;

        var encodingType = element.Attribute("EncodingType")?.Value;
        if (encodingType != "hexBinary")
            throw new ArgumentException($"Invalid encoding type for {elementName}");

        string hexValue = element.Value.Trim();
        return EmvRsaHelper.HexStringToByteArray(hexValue);
    }

    private void ValidateCaKey(RSAParameters key)
    {
        if (key.Modulus.Length != 248)  // 1984 bits
            throw new ArgumentException($"Invalid CA key length: {key.Modulus.Length} bytes (expected 248)");

        if (key.Exponent.Length != 1 || key.Exponent[0] != 0x03)
            throw new ArgumentException("Invalid CA exponent (must be 03)");
    }

    private void ValidateIssuerKey(RSAParameters key)
    {
        if (key.Modulus.Length != 176)  // 1408 bits
            throw new ArgumentException($"Invalid Issuer key length: {key.Modulus.Length} bytes (expected 176)");

        if (key.Exponent.Length != 1 || key.Exponent[0] != 0x03)
            throw new ArgumentException("Invalid Issuer exponent (must be 03)");
    }
}
