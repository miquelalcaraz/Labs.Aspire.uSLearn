using System.Text.Json.Serialization;

namespace uSLearn.Accounts.Domain.OrganizationAggregate
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TaxNumberType
    {
        Unknown = 0,
        // International / generic
        TaxId,
        Vat,

        // United States
        Ein,            // Employer Identification Number
        Tin,            // Taxpayer Identification Number
        Ssn,            // Social Security Number (sole proprietors)

        // Europe
        Nif,            // Spain / Portugal
        Cif,            // Spain (legacy company identifier)
        UstIdNr,        // Germany VAT (Umsatzsteuer-Identifikationsnummer)

        // United Kingdom
        Utr,            // Unique Taxpayer Reference

        // Canada
        Bn,             // Business Number

        // Australia / New Zealand
        Abn,            // Australian Business Number
        Tfn,            // Tax File Number
        Ird,            // NZ Inland Revenue Number

        // Asia-Pacific
        Gst,            // Goods and Services Tax (India, SG, AU, etc.)
        Pan,            // India Permanent Account Number

        // Latin America
        Cnpj,           // Brazil company ID
        Cpf,            // Brazil personal tax ID
        Rfc,            // Mexico Registro Federal de Contribuyentes
        Nit             // Colombia and others
    }

}
