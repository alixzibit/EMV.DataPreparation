using EMV.DataPreparation;
using Multos.Crypto.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EMV.DataPreparation
{
    public class CAKeyInput
    {
        public string RID { get; set; }        // e.g., "A000000912"
        public string KeyIndex { get; set; }    // e.g., "6D"
        public string ModulusN { get; set; }
        public string PublicExponent { get; set; }

        public bool Validate()
        {
            if (string.IsNullOrEmpty(RID))
                throw new ArgumentException("RID is required");

            if (!IsValidRID(RID))
                throw new ArgumentException("Invalid RID format. Must be 10 hex characters");

            if (string.IsNullOrEmpty(KeyIndex))
                throw new ArgumentException("CA Public Key Index is required");

            if (!IsValidKeyIndex(KeyIndex))
                throw new ArgumentException("Invalid CA Public Key Index format. Must be 2 hex characters");

            if (string.IsNullOrEmpty(ModulusN))
                throw new ArgumentException("CA Public Key Modulus is required");

            if (string.IsNullOrEmpty(PublicExponent))
                throw new ArgumentException("CA Public Key Exponent is required");

            return true;
        }

        private bool IsValidRID(string rid)
        {
            // RID must be exactly 5 bytes (10 hex characters)
            return !string.IsNullOrEmpty(rid) &&
                   rid.Length == 10 &&
                   System.Text.RegularExpressions.Regex.IsMatch(rid, "^[0-9A-Fa-f]{10}$");
        }

        private bool IsValidKeyIndex(string index)
        {
            // Index must be exactly 1 byte (2 hex characters)
            return !string.IsNullOrEmpty(index) &&
                   index.Length == 2 &&
                   System.Text.RegularExpressions.Regex.IsMatch(index, "^[0-9A-Fa-f]{2}$");
        }

        public string GetFullIdentifier()
        {
            // Returns combined RID + Index
            return $"{RID}{KeyIndex}";
        }

        private bool IsValidModulusLength(int bytes)
        {


            return 
                   bytes == 96 || // 768 bits
                   bytes == 128 || // 1024 bits
                   bytes == 144 || // 1152 bits
                   bytes == 176 || // 1408 bits
                   bytes == 248;   // 1984 bits
        }
    }

    public class IssuerKeyInput
    {
        public string IssuerIdentificationNumber { get; set; }
        public string Certificate { get; set; }
        public string ModulusN { get; set; }
        public string PublicExponent { get; set; }
        public IssuerPrivateKey PrivateKey { get; set; }

        // Certificate expiry and serial number
        public string ExpiryDate { get; set; }  // YYMM format
        public string SerialNumber { get; set; }

        public bool Validate(CAKeyInput caKey)
        {
            // IIN must be 6-8 digits
            if (!Regex.IsMatch(IssuerIdentificationNumber, "^[0-9]{6,8}$"))
                throw new ArgumentException("Invalid IIN format");

            // Issuer modulus must be smaller than CA modulus
            if ((ModulusN.Length / 2) >= (caKey.ModulusN.Length / 2))
                throw new ArgumentException("Issuer key length must be smaller than CA key length");

            // Expiry date format check (YYMM)
            if (!Regex.IsMatch(ExpiryDate, "^[0-9]{4}$"))
                throw new ArgumentException("Invalid expiry date format");

            return true;
        }
    }

    public class IssuerPrivateKey
    {
        public string PrimeP { get; set; }
        public string PrimeQ { get; set; }
        public string PrimeExponentDP { get; set; }
        public string PrimeExponentDQ { get; set; }
        public string CrtCoefficientU { get; set; }

        public bool Validate(string modulusN)
        {
            // CRT components must be half the length of modulus
            int expectedLength = (modulusN.Length / 4);  // Half of modulus in bytes

            if (PrimeP.Length != expectedLength ||
                PrimeQ.Length != expectedLength ||
                PrimeExponentDP.Length != expectedLength ||
                PrimeExponentDQ.Length != expectedLength ||
                CrtCoefficientU.Length != expectedLength)
            {
                throw new ArgumentException("Invalid CRT component lengths");
            }

            return true;
        }
    }

    public class KeyImportOptions
    {
        public bool ValidateCertificates { get; set; } = true;
        public bool StorePrivateKeys { get; set; } = false;
        public string KeyStorePath { get; set; }
        public bool UseSecureStorage { get; set; } = true;
        public string[] AllowedHashAlgorithms { get; set; } = new[] { "SHA1" };
        public string[] AllowedRsaExponents { get; set; } = new[] { "03", "010001" };
    }
    public class CertificateInput
    {
        // CA (Certificate Authority) Data
        public string CaModulus { get; set; }
        public string CaExponent { get; set; }
        public string CaIndex { get; set; }  // RID + Index
        public string CaKeyFile { get; set; }      // Path to CA key XML
        public string IssuerKeyFile { get; set; }  // Path to Issuer key XML
        public string IccKeyFile { get; set; }     // Optional path to ICC key XML

        // Issuer Data
        public string IssuerModulus { get; set; }
        public string IssuerExponent { get; set; }
        public IssuerPrivateKeyComponents IssuerPrivateKey { get; set; }
        public string IssuerPrivateExponent { get; set; }
        public string IssuerCertificate { get; set; }  // Optional, existing certificate
        public string IssuerExpiryDate { get; set; }   // YYMM format
        public string IssuerIdentifier { get; set; }   // Usually first 6-8 digits of PAN

        // ICC Certificate Data
        public string IccExpiryDate { get; set; }      // YYMM format
        public bool UseExistingIccKey { get; set; }
        public string ExistingIccKeyFile { get; set; } // Optional, path to existing ICC key file

        public bool Validate()
        {
            if (string.IsNullOrEmpty(CaModulus))
                throw new ArgumentException("CA Modulus is required");

            if (string.IsNullOrEmpty(CaExponent))
                throw new ArgumentException("CA Exponent is required");

            if (string.IsNullOrEmpty(IssuerModulus))
                throw new ArgumentException("Issuer Modulus is required");

            if (string.IsNullOrEmpty(IssuerExponent))
                throw new ArgumentException("Issuer Exponent is required");

            if (UseExistingIccKey && string.IsNullOrEmpty(ExistingIccKeyFile))
                throw new ArgumentException("ICC Key file path is required when using existing key");

            // Validate date formats
            if (!IsValidDate(IssuerExpiryDate))
                throw new ArgumentException("Invalid Issuer expiry date format (YYMM required)");

            if (!IsValidDate(IccExpiryDate))
                throw new ArgumentException("Invalid ICC expiry date format (YYMM required)");

            return true;
        }
        public class IssuerPrivateKeyComponents
        {
            public string PrivateExponent { get; set; }  // D
            public string PrimeP { get; set; }
            public string PrimeQ { get; set; }
            public string PrimeExponentDP { get; set; }  // D mod (P-1)
            public string PrimeExponentDQ { get; set; }  // D mod (Q-1)
            public string CrtCoefficient { get; set; }   // Q^-1 mod P
        }

        private bool IsValidDate(string date)
        {
            if (string.IsNullOrEmpty(date) || date.Length != 4)
                return false;

            if (!int.TryParse(date.Substring(0, 2), out int year))
                return false;

            if (!int.TryParse(date.Substring(2, 2), out int month))
                return false;

            return month >= 1 && month <= 12;
        }
    }
    public class CardInput
    {
        public string Pan { get; set; }
        public string Psn { get; set; }
        public string ExpiryDate { get; set; }
        public int IccKeyLength { get; set; } = 768;
        public KeyDerivationOption DerivationOption { get; set; }
        public MasterKeysInput MasterKeys { get; set; }
        public bool UseExistingIccKey { get; set; }
        public QSparcKeyGenerator.RsaKeyParameters ExistingIccKey { get; set; }
    }

    public class MasterKeysInput
    {
        public string ImkAc { get; set; }
        public string ImkSmi { get; set; }
        public string ImkSmc { get; set; }
    }
}
public static class CardInputExtensions
{
    public static bool ValidateCardInput(this CardInput input)
    {
        if (input == null)
            return false;

        if (string.IsNullOrEmpty(input.Pan))
            return false;

        if (string.IsNullOrEmpty(input.Psn))
            return false;

        if (input.UseExistingIccKey && input.ExistingIccKey == null)
            return false;

        // Validate ICC key if present
        if (input.ExistingIccKey != null)
        {
            if (input.ExistingIccKey.Modulus == null ||
                input.ExistingIccKey.Modulus.Length != 96)  // 768 bits
                return false;

            if (input.ExistingIccKey.PublicExponent == null ||
                input.ExistingIccKey.PublicExponent.Length != 1 ||
                input.ExistingIccKey.PublicExponent[0] != 0x03)
                return false;
        }

        return true;
    }
}

//public class CertificateInput
//{
//    public string CaModulus { get; set; }
//    public string CaExponent { get; set; }
//    public string IssuerModulus { get; set; }
//    public string IssuerExponent { get; set; }
//    public string IssuerPrivateExponent { get; set; }
//    public string IssuerExpiryDate { get; set; }
//    public string IccExpiryDate { get; set; }
//    public bool UseExistingIccKey { get; set; }
//}
