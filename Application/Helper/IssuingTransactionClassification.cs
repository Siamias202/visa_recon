using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace VISA_RECON.API.Application.Helper;

public sealed record IssuingClassification(
    string Currency,
    string Category);

public static class IssuingTransactionClassification
{
    public const string RuleVersion = "ISSUING_V2_1";

    private static readonly IReadOnlyDictionary<string, IssuingClassification>
        CbsAccounts = new Dictionary<string, IssuingClassification>(
            StringComparer.Ordinal)
        {
            ["9900832418050"] = new("BDT", "ATM"),
            ["9900832428050"] = new("BDT", "POS"),
            ["9900832392840"] = new("USD", "POS"),
            ["9900832393840"] = new("USD", "PREAUTH"),
            ["9900832394840"] = new("USD", "ATM")
        };

    public static IssuingClassification ClassifyCbs(string? accountNumber)
    {
        var normalized = Normalize(accountNumber);

        if (normalized is not null
            && CbsAccounts.TryGetValue(normalized, out var classification))
        {
            return classification;
        }

        throw new InvalidDataException(
            $"CBS account number '{accountNumber}' is not configured for " +
            "issuing reconciliation.");
    }

    public static IssuingClassification ClassifyBo(
        string? transactionCurrency,
        string? transactionType)
    {
        var normalizedCurrency = NormalizeUpper(transactionCurrency)
            ?? throw new InvalidDataException(
                "BO transaction currency is required.");
        var normalizedType = NormalizeUpper(transactionType)
            ?? throw new InvalidDataException(
                "BO transaction type is required.");

        var category = normalizedType switch
        {
            "ATM CASH WITHDRAWAL" => "ATM",
            "PURCHASE" or "POS" or "POS PURCHASE" or "POS/PURCHASE" => "POS",
            "PREAUTH" or "PRE-AUTH" or "PRE AUTH" => "PREAUTH",
            _ => throw new InvalidDataException(
                $"BO transaction type '{transactionType}' is not supported " +
                "for issuing reconciliation.")
        };

        // Per the issuing GL structure, BDT settles to BDT and every foreign
        // transaction currency settles through the USD reconciliation GL.
        var reconciliationCurrency = normalizedCurrency == "BDT"
            ? "BDT"
            : "USD";

        return new IssuingClassification(
            reconciliationCurrency,
            category);
    }

    public static byte[]? CreatePrimaryKey(
        IssuingClassification classification,
        string? utrnno,
        string? rrn,
        string? authCode,
        decimal? amount)
    {
        var normalizedUtrnno = NormalizeUpper(utrnno);
        var normalizedRrn = NormalizeUpper(rrn);
        var normalizedAuthCode = NormalizeUpper(authCode);

        if (normalizedUtrnno is null
            || normalizedRrn is null
            || normalizedAuthCode is null
            || amount is null)
        {
            return null;
        }

        return Hash(
            classification.Currency,
            classification.Category,
            normalizedUtrnno,
            normalizedRrn,
            normalizedAuthCode,
            FormatAmount(amount.Value));
    }

    public static byte[]? CreateSecondaryKey(
        IssuingClassification classification,
        string? utrnno,
        string? rrn,
        string? authCode,
        decimal? amount)
    {
        // Secondary matching is allowed only when both primary identifiers are
        // absent. A partly populated primary identity is not a secondary match.
        if (Normalize(utrnno) is not null || Normalize(rrn) is not null)
        {
            return null;
        }

        var normalizedAuthCode = NormalizeUpper(authCode);

        if (normalizedAuthCode is null || amount is null)
        {
            return null;
        }

        return Hash(
            classification.Currency,
            classification.Category,
            normalizedAuthCode,
            FormatAmount(amount.Value));
    }

    public static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeUpper(string? value) =>
        Normalize(value)?.ToUpperInvariant();

    private static string FormatAmount(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);

    private static byte[] Hash(params string[] parts)
    {
        var canonicalValue = string.Join('\u001F', parts);
        return SHA256.HashData(Encoding.UTF8.GetBytes(canonicalValue));
    }
}
