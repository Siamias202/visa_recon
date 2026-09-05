using CsvHelper;
using System.Globalization;
using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.BOTransaction;
using VISA_RECON.API.Application.Helper;
using VISA_RECON.API.Application.Interfaces.Repository;
using VISA_RECON.API.Application.Interfaces.Services;
using VISA_RECON.API.Application.Mappings.BOMappingsHelper;
using VISA_RECON.API.Infrastructure.Persistence;
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

        public async Task<Result<Unit>> ValidateAndMergeAsync(
                                                                 List<IFormFile> files)
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
                    if (file == null)
                    {
                        continue;
                    }

                    if (file.Length == 0)
                    {
                        return Result<Unit>.Failure(
                            "BO002",
                            $"File '{file.FileName}' is empty.");
                    }

                    var extension = Path
                        .GetExtension(file.FileName)
                        .ToLowerInvariant();

                    switch (extension)
                    {
                        case ".csv":

                            await BOUploadHelper.ReadCsvFileAsync(
                                file,
                                mergedRecords);

                            break;

                        case ".xlsx":

                            await BOUploadHelper.ReadXlsxFileAsync(
                                file,
                                mergedRecords);

                            break;

                        default:

                            return Result<Unit>.Failure(
                                "BO006",
                                $"Unsupported file format for '{file.FileName}'. " +
                                "Only CSV and XLSX files are allowed.");
                    }
                }

                if (mergedRecords.Count == 0)
                {
                    return Result<Unit>.Failure(
                        "BO005",
                        "No transaction records found.");
                }

                mergedRecords.RemoveAll(IssuingUploadCleaning.ShouldRemove);

                if (mergedRecords.Count == 0)
                {
                    return Result<Unit>.Failure(
                        "BO005",
                        "No eligible BO transaction records found.");
                }

                var inserted = await _repository.InsertBulkAsync(
                    mergedRecords);

                return Result<Unit>.Success(
                    APIResponseCodes.SUCCESS_CODE,
                    $"{inserted} BO transaction records uploaded successfully.");
            }
            catch (InvalidDataException ex)
            {
                return Result<Unit>.Failure(
                    "BO004",
                    ex.Message);
            }
            catch (Exception ex)
            {
                return Result<Unit>.Failure(
                    APIResponseCodes.ERROR_CODE,
                    $"BO transaction upload failed: {ex.Message}");
            }
        }



        private static readonly HashSet<string> ExpectedHeaders =
                        new(StringComparer.OrdinalIgnoreCase)
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
                            "AUTH"
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

        public async Task<Result<PagedResponse<BOTransactionDetailsResponse>>> GetBOTransactionsListAsync(BOTransactionRequest request)
        {
            request ??= new BOTransactionRequest();

            try
            {
                var pagedResponse = await _repository.GetBOTransactionDetailsListAsync(request);

                return Result<PagedResponse<BOTransactionDetailsResponse>>.Success(
                    APIResponseCodes.SUCCESS_CODE,
                    "BO transaction details retrieved successfully.",
                    pagedResponse);
            }
            catch (Exception ex)
            {
                return Result<PagedResponse<BOTransactionDetailsResponse>>.Failure(
                    APIResponseCodes.ERROR_CODE,
                    $"Failed to retrieve BO transactions. Error: {ex.Message}");
            }
        }
    }
}
