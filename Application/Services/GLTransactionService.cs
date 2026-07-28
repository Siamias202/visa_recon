using System.Globalization;
using CsvHelper;
using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.GLTransaction;
using VISA_RECON.API.Application.Interfaces.Repositories;
using VISA_RECON.API.Application.Interfaces.Services;
using VISA_RECON.API.Application.Mappings;
using static VISA_RECON.API.Application.Constants.Constants;

public class GLTransactionService : IGLTransactionService
{
    private readonly IGLTransactionRepository _repository;

    public GLTransactionService(
        IGLTransactionRepository repository)
    {
        _repository = repository;
    }


    public async Task<Result<Unit>> ValidateAndMergeAsync(List<IFormFile> files)
    {
        if (files == null || files.Count == 0)
        {
            return Result<Unit>.Failure(
                "GL001",
                "No files were uploaded.");
        }


        var mergedRecords = new List<UploadGLRequest>();

        try
        {
            foreach (var file in files)
            {
                if (file.Length == 0)
                {
                    return Result<Unit>.Failure(
                        "GL002",
                        $"File '{file.FileName}' is empty.");
                }


                using var stream = file.OpenReadStream();
                using var reader = new StreamReader(stream);

                using var csv = new CsvReader(
                    reader,
                    CultureInfo.InvariantCulture);


                if (!csv.Read())
                {
                    return Result<Unit>.Failure(
                        "GL003",
                        $"File '{file.FileName}' has no data.");
                }


                csv.ReadHeader();

                var headers = csv.HeaderRecord;


                if (!IsValidGLTransaction(headers))
                {
                    return Result<Unit>.Failure(
                        "GL004",
                        $"Invalid header format in file '{file.FileName}'.");
                }


                // Reset stream for actual reading
                stream.Position = 0;


                using var reader2 = new StreamReader(stream);

                using var csv2 = new CsvReader(
                    reader2,
                    CultureInfo.InvariantCulture);


                csv2.Context.RegisterClassMap<UploadGLRequestMappings>();


                foreach (var record in csv2.GetRecords<UploadGLRequest>())
                {
                    mergedRecords.Add(record);
                }
            }


            if (mergedRecords.Count == 0)
            {
                return Result<Unit>.Failure(
                    "GL005",
                    "No transaction records found.");
            }


            await _repository.InsertBulkAsync(mergedRecords);


            return Result<Unit>.Success(
                APIResponseCodes.SUCCESS_CODE,
                $"{mergedRecords.Count} GL transaction records uploaded successfully.");
        }
        catch (Exception ex)
        {
            return Result<Unit>.Failure(
                APIResponseCodes.ERROR_CODE,
                $"GL transaction upload failed: {ex.Message}");
        }
    }


    private static readonly HashSet<string> ExpectedHeaders = new()
    {
        "ACCOUNT NO",
        "POSTING DATE",
        "VALUE DATE",
        "BATCH ID",
        "POSTING BRANCH",
        "UNIQUEREFERENCENO",
        "DEBIT/CREDIT",
        "AMOUNT",
        "TRANSACTION CODE",
        "TRANSACTION NAME",
        "CURRENCY",
        "TIME STAMP",
        "UNIQUE ID",
        "NARRATIVE 1",
        "NARRATIVE 2",
        "RRN",
        "AUTH CODE",
        "NARRATIVE 3",
        "NARRATIVE 4"
    };


    private static bool IsValidGLTransaction(string[] headers)
    {
        if (headers == null)
            return false;


        var normalizedHeaders = headers
            .Select(x => x.Trim().ToUpper())
            .ToHashSet();


        return ExpectedHeaders.SetEquals(normalizedHeaders);
    }
}