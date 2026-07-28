using System.Globalization;
using CsvHelper;
using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.BOTransaction;
using VISA_RECON.API.Application.Interfaces.Repository;
using VISA_RECON.API.Application.Interfaces.Services;
using VISA_RECON.API.Application.Mappings;
using static VISA_RECON.API.Application.Constants.Constants;

namespace VISA_RECON.API.Application.Services
{
    public class BOTransactionService : IBOTransactionService
    {
        private readonly IBOTransactionRepository _repository;

        public BOTransactionService(IBOTransactionRepository repository)
        {
            _repository = repository;
        }

        public async Task<Result<Unit>> ValidateAndMergeAsync(List<IFormFile> files)
        {
            if (files == null || files.Count == 0)
            {
                return Result<Unit>.Failure(
                    "BO001",
                    "No files were uploaded.");
            }

            var mergedRecords = new List<UploadBORequest>();

            try
            {
                foreach (var file in files)
                {
                    if (file.Length == 0)
                    {
                        return Result<Unit>.Failure(
                            "BO002",
                            $"File '{file.FileName}' is empty.");
                    }

                    using var stream = file.OpenReadStream();
                    using var reader = new StreamReader(stream);

                    using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

                    if (!csv.Read())
                    {
                        return Result<Unit>.Failure(
                            "BO003",
                            $"File '{file.FileName}' has no data.");
                    }

                    csv.ReadHeader();

                    var headers = csv.HeaderRecord;

                    if (!IsValidBOTransaction(headers))
                    {
                        return Result<Unit>.Failure(
                            "BO004",
                            $"Invalid header format in file '{file.FileName}'.");
                    }

                    // Reset stream
                    stream.Position = 0;

                    using var reader2 = new StreamReader(stream);
                    using var csv2 = new CsvReader(reader2, CultureInfo.InvariantCulture);

                    csv2.Context.RegisterClassMap<UploadBORequestMappings>();

                    foreach (var record in csv2.GetRecords<UploadBORequest>())
                    {
                        mergedRecords.Add(record);
                    }
                }

                if (mergedRecords.Count == 0)
                {
                    return Result<Unit>.Failure(
                        "BO005",
                        "No transaction records found.");
                }

                await _repository.InsertBulkAsync(mergedRecords);

                return Result<Unit>.Success(
                    APIResponseCodes.SUCCESS_CODE,
                    $"{mergedRecords.Count} BO transaction records uploaded successfully.");
            }
            catch (Exception ex)
            {
                return Result<Unit>.Failure(
                    APIResponseCodes.ERROR_CODE,
                    $"BO transaction upload failed: {ex.Message}");
            }
        }

        private static readonly HashSet<string> ExpectedHeaders = new()
        {
            "SESSION_ID",
            "BO_OPER_ID",
            "EP_STTL_DATE",
            "RUN_DATE",
            "TRX_TYPE",
            "MESSAGE_TYPE",
            "CLR_STATUS",
            "CONTRACT_TYPE",
            "CARD_NUMBER",
            "ACCOUNT_NUMBER",
            "SENDER_ACCOUNT_NUMBER",
            "AUTH_CODE",
            "ARN",
            "TRANS_DATE",
            "CLR_TXN_AMOUNT",
            "TXN_CURRENCY",
            "BILL_AMT",
            "ACCT_CURR",
            "STTL_AMOUNT",
            "ST_REV",
            "MATCH_STATUS",
            "AUTH_ID",
            "MCC",
            "MERCHANT_NUMBER",
            "TERMINAL_NUMBER",
            "MERCHANT_NAME",
            "MERCHANT_CITY",
            "MERCHANT_COUNTRY",
            "AUTH_OPR_ID",
            "BASE_II_ID",
            "TRANSACTION_DATE",
            "AUTH_CARD_NUMBER",
            "REVERSAL_FLAG",
            "TXN_AMOUNT",
            "AUTH_CURRENCY",
            "BILLING_AMOUNT",
            "FEES",
            "BILLING_CURRENCY",
            "STATUS",
            "TXN_TYPE",
            "AUTH_MESSAGE_TYPE",
            "AUTH_MCC",
            "AUTH_MID",
            "AUTH_MERCHANT_NAME",
            "AUTH_CITY",
            "AUTH_COUNTRY",
            "AUTH_TID",
            "AUTH_ACCT_UMBER",
            "POS_COND_CODE",
            "UTRNNO",
            "TRACE_NUMBER",
            "TRACE_TO_CBS",
            "RRN",
            "AUTH",
            "FE",
            "VROL",
            "Status",
            "update"
        };

        private static bool IsValidBOTransaction(string[] headers)
        {
            if (headers == null)
                return false;

            var normalizedHeaders = headers
                .Select(x => x.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return ExpectedHeaders.SetEquals(normalizedHeaders);
        }
    }
}