namespace VISA_RECON.API.Application.Helper;

public static class IssuingReconciliationFilter
{
    private sealed record AccountMapping(
        string Currency,
        string Category,
        string AccountNumber);

    private static readonly AccountMapping[] AccountMappings =
    [
        new("USD", "ATM", "9900832394840"),
        new("BDT", "ATM", "9900832418050"),
        new("USD", "POS", "9900832392840"),
        new("BDT", "POS", "9900832428050"),
        new("USD", "PREAUTH", "9900832393840")
    ];

    public static string? NormalizeCurrency(string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            return null;
        }

        return currency.Trim().ToUpperInvariant() switch
        {
            "USD" => "USD",
            "BDT" => "BDT",
            _ => throw new InvalidDataException(
                $"Unsupported currency '{currency}'. Allowed values: USD, BDT.")
        };
    }

    public static string? NormalizeCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return null;
        }

        return category.Trim().ToUpperInvariant() switch
        {
            "ATM" => "ATM",
            "POS" or "POS-PURCHASE" or "POS PURCHASE" or "POS/PURCHASE"
                or "PURCHASE" => "POS",
            "PREAUTH" or "PRE-AUTH" or "PRE AUTH" => "PREAUTH",
            _ => throw new InvalidDataException(
                $"Unsupported category '{category}'. " +
                "Allowed values: ATM, POS, PREAUTH.")
        };
    }

    public static string[] ResolveAccountNumbers(
        string? accountNumber,
        string? currency,
        string? category)
    {
        if (category is not null && currency is null)
        {
            throw new InvalidDataException(
                "Currency is required when Category is provided.");
        }

        var explicitAccountNumber = string.IsNullOrWhiteSpace(accountNumber)
            ? null
            : accountNumber.Trim();

        if (currency is null && category is null)
        {
            return explicitAccountNumber is null
                ? []
                : [explicitAccountNumber];
        }

        if (category == "PREAUTH" && currency != "USD")
        {
            throw new InvalidDataException(
                "Pre-Auth is only available for USD.");
        }

        var mappedAccountNumbers = AccountMappings
            .Where(mapping => mapping.Currency == currency)
            .Where(mapping => category is null || mapping.Category == category)
            .Select(mapping => mapping.AccountNumber)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (mappedAccountNumbers.Length == 0)
        {
            throw new InvalidDataException(
                $"Category '{category}' is not available for currency '{currency}'.");
        }

        if (explicitAccountNumber is null)
        {
            return mappedAccountNumbers;
        }

        if (!mappedAccountNumbers.Contains(
                explicitAccountNumber,
                StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"Account number '{explicitAccountNumber}' does not belong to " +
                $"currency '{currency}' and category '{category ?? "ALL"}'.");
        }

        return [explicitAccountNumber];
    }

    public static string[] ResolveBoTransactionTypes(string? category)
    {
        return category switch
        {
            null => [],
            "ATM" => ["ATM CASH WITHDRAWAL"],
            "POS" => ["PURCHASE"],
            "PREAUTH" => ["PREAUTH", "PRE-AUTH", "PRE AUTH"],
            _ => throw new InvalidDataException(
                $"Unsupported normalized category '{category}'.")
        };
    }
}
