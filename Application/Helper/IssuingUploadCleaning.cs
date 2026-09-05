using System.Globalization;
using VISA_RECON.API.Application.DTOs.BOTransaction;
using VISA_RECON.API.Application.DTOs.GLTransaction;

namespace VISA_RECON.API.Application.Helper;

public static class IssuingUploadCleaning
{
    private static readonly HashSet<string> ExcludedGlTransactionCodes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "020", "R07", "R10", "R26", "R28", "R33", "R34", "R44", "R46","20"
        };

    private static readonly HashSet<string> ExcludedBoTransactionTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Purchase return (Credit)",
            "Payment Transaction",
            "P2P Credit"
        };

    public static bool ShouldRemove(UploadGLRequest row) =>
        ExcludedGlTransactionCodes.Contains(row.TransactionCode.Trim());

    public static bool ShouldRemove(UploadBORequest row)
    {
        var transactionType = row.TRX_TYPE.Trim();

        if (ExcludedBoTransactionTypes.Contains(transactionType))
            return true;

        if (string.Equals(
                row.MESSAGE_TYPE.Trim(),
                "Representment",
                StringComparison.OrdinalIgnoreCase))
            return true;

        return !string.Equals(
                   transactionType,
                   "ATM Cash withdrawal",
                   StringComparison.OrdinalIgnoreCase)
               && IsOne(row.ST_REV);
    }

    private static bool IsOne(string value) =>
        decimal.TryParse(
            value.Trim(),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var parsed)
        && parsed == 1m;
}
