
using System;
using System.Xml.Linq;
using System.Security.Cryptography;
using Multos.Crypto.Core;

namespace EMV.DataPreparation
{
    public class RsaKeyLoader
    {
        public class RsaKeyComponents
        {
            public byte[] Modulus { get; set; }
            public byte[] Exponent { get; set; }
            public byte[] P { get; set; }
            public byte[] Q { get; set; }
            public byte[] DP { get; set; }
            public byte[] DQ { get; set; }
            public byte[] InverseQ { get; set; }
            public byte[] D { get; set; }
        }

        public static RsaKeyComponents LoadFromXml(string xmlFilePath)
        {
            try
            {
                var doc = XDocument.Load(xmlFilePath);
                var root = doc.Root;

                if (root.Name != "RSAKeyPair")
                    throw new ArgumentException("Invalid XML format: Root element must be 'RSAKeyPair'");

                var components = new RsaKeyComponents
                {
                    Modulus = GetComponentValue(root, "Modulus"),
                    Exponent = GetComponentValue(root, "Exponent"),
                    P = GetComponentValue(root, "P"),
                    Q = GetComponentValue(root, "Q"),
                    DP = GetComponentValue(root, "DP"),
                    DQ = GetComponentValue(root, "DQ"),
                    InverseQ = GetComponentValue(root, "InverseQ"),
                    D = GetComponentValue(root, "D")
                };

                // Validate components
                ValidateKeyComponents(components);

                return components;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load RSA key from XML: {ex.Message}", ex);
            }
        }

        private static byte[] GetComponentValue(XElement root, string elementName)
        {
            var element = root.Element(elementName);
            if (element == null)
                throw new ArgumentException($"Missing {elementName} element");

            var encodingType = element.Attribute("EncodingType")?.Value;
            if (encodingType != "hexBinary")
                throw new ArgumentException($"Invalid encoding type for {elementName}");

            string hexValue = element.Value.Trim();
            return EmvRsaHelper.HexStringToByteArray(hexValue);
        }

        private static void ValidateKeyComponents(RsaKeyComponents components)
        {
            if (components.Modulus == null || components.Modulus.Length == 0)
                throw new ArgumentException("Invalid modulus");

            if (components.Exponent == null || components.Exponent.Length == 0)
                throw new ArgumentException("Invalid exponent");

            // For EMV, verify exponent is 03
            if (components.Exponent.Length != 1 || components.Exponent[0] != 0x03)
                throw new ArgumentException("Invalid exponent for EMV (must be 03)");

            // Validate lengths for EMV
            if (components.Modulus.Length != 96)  // 768 bits
                throw new ArgumentException($"Invalid modulus length for ICC key: {components.Modulus.Length} bytes (expected 96)");
        }

        public static QSparcKeyGenerator.RsaKeyParameters ConvertToKeyParameters(RsaKeyComponents components)
        {
            return new QSparcKeyGenerator.RsaKeyParameters
            {
                Modulus = components.Modulus,
                PublicExponent = components.Exponent,
                PrivateExponent = components.D,
                P = components.P,
                Q = components.Q,
                DP = components.DP,
                DQ = components.DQ,
                InverseQ = components.InverseQ
            };
        }

        public static RSAParameters ConvertToRSAParameters(RsaKeyComponents components)
        {
            return new RSAParameters
            {
                Modulus = components.Modulus,
                Exponent = components.Exponent,
                D = components.D,
                P = components.P,
                Q = components.Q,
                DP = components.DP,
                DQ = components.DQ,
                InverseQ = components.InverseQ
            };
        }
    }
}
