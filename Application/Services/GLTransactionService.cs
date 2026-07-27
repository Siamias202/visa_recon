using CsvHelper;
using System.Globalization;
using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.GLTransaction;
using VISA_RECON.API.Application.Interfaces.Repositories;
using VISA_RECON.API.Application.Interfaces.Services;
using VISA_RECON.API.Application.Mappings;

public class GLTransactionService : IGLTransactionService
{

    private readonly IGLTransactionRepository _repository;

    public GLTransactionService(
        IGLTransactionRepository repository)
    {
        _repository = repository;
    }

    public async Task<bool> ValidateAndMergeAsync(List<IFormFile> files)
    {
        var mergedRecords = new List<UploadRequest>();

        foreach (var file in files)
        {
            using var stream = file.OpenReadStream();
            using var reader = new StreamReader(stream);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

            csv.Read();
            csv.ReadHeader();

            var headers = csv.HeaderRecord;

            if (!IsValidGLTransaction(headers))
            {
                return false;
            }

            stream.Position = 0;

            using var reader2 = new StreamReader(stream);
            using var csv2 = new CsvReader(reader2, CultureInfo.InvariantCulture);

            csv2.Context.RegisterClassMap<UploadRequestMappings>();

            var records = csv2.GetRecords<UploadRequest>().ToList();

            mergedRecords.AddRange(records);
        }

        try
        {
            await _repository.InsertBulkAsync(mergedRecords);

            return true;
        }
        catch
        {
            return false;
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
        return ExpectedHeaders.SetEquals(headers);
    }

    
}