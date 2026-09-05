using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.GLTransaction;
using VISA_RECON.API.Application.Helper;
using VISA_RECON.API.Application.Interfaces.Repositories;
using VISA_RECON.API.Application.Interfaces.Services;
using static VISA_RECON.API.Application.Constants.Constants;

public class GLTransactionService : IGLTransactionService
{
    private readonly IGLTransactionRepository _repository;

    public GLTransactionService(
        IGLTransactionRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<Unit>> ValidateAndMergeAsync(
        List<IFormFile> files)
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
                if (file == null)
                {
                    return Result<Unit>.Failure(
                        "GL002",
                        "Invalid file.");
                }

                if (file.Length == 0)
                {
                    return Result<Unit>.Failure(
                        "GL002",
                        $"File '{file.FileName}' is empty.");
                }

                var extension = Path
                    .GetExtension(file.FileName)
                    .ToLowerInvariant();

                switch (extension)
                {
                    case ".csv":

                        await GLUploadHelper.ReadCsvFileAsync(
                            file,
                            mergedRecords);

                        break;

                    case ".xlsx":

                        await GLUploadHelper.ReadXlsxFileAsync(
                            file,
                            mergedRecords);

                        break;

                    default:

                        return Result<Unit>.Failure(
                            "GL006",
                            $"Unsupported file format '{extension}' " +
                            $"for file '{file.FileName}'. " +
                            "Only CSV and XLSX files are supported.");
                }
            }

            mergedRecords.RemoveAll(IssuingUploadCleaning.ShouldRemove);

            if (mergedRecords.Count == 0)
            {
                return Result<Unit>.Failure(
                    "GL005",
                    "No transaction records found.");
            }

            var inserted = await _repository.InsertBulkAsync(
                mergedRecords);

            return Result<Unit>.Success(
                APIResponseCodes.SUCCESS_CODE,
                $"{inserted} GL transaction records uploaded successfully.");
        }
        catch (InvalidDataException ex)
        {
            return Result<Unit>.Failure(
                "GL007",
                ex.Message);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);

            return Result<Unit>.Failure(
                APIResponseCodes.ERROR_CODE,
                $"GL transaction upload failed: {ex.Message}");
        }
    }

    public async Task<Result<PagedResponse<GLTransactionDetailsResponse>>>
        GetGLTransactionDetailsAsync(
            GLTransactionRequest request)
    {
        request ??= new GLTransactionRequest();

        try
        {
            var pagedResponse =
                await _repository.GetGLTransactionDetailsListAsync(
                    request);

            return Result<PagedResponse<GLTransactionDetailsResponse>>
                .Success(
                    APIResponseCodes.SUCCESS_CODE,
                    APIResponseMessages.SUCCESS_MSG,
                    pagedResponse);
        }
        catch (InvalidDataException ex)
        {
            return Result<PagedResponse<GLTransactionDetailsResponse>>
                .Failure(
                    APIResponseCodes.ERROR_CODE,
                    ex.Message);
        }
        catch (Exception ex)
        {
            return Result<PagedResponse<GLTransactionDetailsResponse>>
                .Failure(
                    APIResponseCodes.ERROR_CODE,
                    $"Failed to retrieve GL transaction details: {ex.Message}");
        }
    }
}
