using VISA_RECON.API.Application.Common;
using VISA_RECON.API.Application.DTOs.AcquiringTransaction;
using VISA_RECON.API.Application.DTOs.GLTransaction;
using VISA_RECON.API.Application.Helper;
using VISA_RECON.API.Application.Interfaces.Repository;
using VISA_RECON.API.Application.Interfaces.Services;
using static VISA_RECON.API.Application.Constants.Constants;

namespace VISA_RECON.API.Application.Services;

public sealed class AcquiringTransactionService : IAcquiringTransactionService
{
    private readonly IAcquiringTransactionRepository _repository;
    public AcquiringTransactionService(IAcquiringTransactionRepository repository) => _repository = repository;

    public async Task<Result<Unit>> UploadGlAsync(List<IFormFile> files)
    {
        var rows = new List<UploadGLRequest>();
        return await UploadAsync(files, async file =>
        {
            if (Path.GetExtension(file.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase))
                await GLUploadHelper.ReadCsvFileAsync(file, rows);
            else if (Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
                await GLUploadHelper.ReadXlsxFileAsync(file, rows);
            else throw new InvalidDataException("Only CSV and XLSX files are supported.");
        }, () => _repository.InsertGlAsync(rows), () => rows.Count, "acquiring GL");
    }

    public async Task<Result<Unit>> UploadFeAsync(List<IFormFile> files)
    {
        var rows = new List<AcquiringFeTransaction>();
        return await UploadAsync(files, async f => rows.AddRange(await AcquiringUploadHelper.ReadFeAsync(f)),
            () => _repository.InsertFeAsync(rows), () => rows.Count, "acquiring FE");
    }

    public async Task<Result<Unit>> UploadEpAsync(List<IFormFile> files)
    {
        var rows = new List<AcquiringEpTransaction>();
        var seen = new HashSet<EpTransactionKey>();

        return await UploadAsync(files, async file =>
        {
            var uploadedRows = await AcquiringUploadHelper.ReadEpAsync(file);
            foreach (var row in uploadedRows)
            {
                if (seen.Add(EpTransactionKey.From(row)))
                    rows.Add(row);
            }
        },
            () => _repository.InsertEpAsync(rows), () => rows.Count, "acquiring EP");
    }

    private readonly record struct EpTransactionKey(
        string Pan,
        string Rrn,
        string Acq,
        string Integratedp,
        string Aymen,
        string Tsyste,
        string M,
        decimal AmountBdt,
        string Currency,
        decimal AmountUsd)
    {
        public static EpTransactionKey From(AcquiringEpTransaction row) =>
            new(
                row.Pan,
                row.Rrn,
                row.Acq,
                row.Integratedp,
                row.Aymen,
                row.Tsyste,
                row.M,
                row.AmountBdt,
                row.Currency,
                row.AmountUsd);
    }

    private static async Task<Result<Unit>> UploadAsync(List<IFormFile> files,
        Func<IFormFile, Task> read, Func<Task<int>> insert, Func<int> count, string name)
    {
        if (files is null || files.Count == 0)
            return Result<Unit>.Failure("ACQ001", "No files were uploaded.");
        try
        {
            foreach (var file in files) await read(file);
            if (count() == 0) return Result<Unit>.Failure("ACQ002", "No transaction records found.");
            var inserted = await insert();
            if (inserted == 0)
                return Result<Unit>.Failure("ACQ002", "No transaction records matched the upload criteria.");
            return Result<Unit>.Success(APIResponseCodes.SUCCESS_CODE,
                $"{inserted} {name} transaction records uploaded successfully.");
        }
        catch (Exception ex)
        {
            return Result<Unit>.Failure(APIResponseCodes.ERROR_CODE, $"{name} upload failed: {ex.Message}");
        }
    }

    public Task<Result<PagedResponse<GLTransactionDetailsResponse>>> GetGlAsync(AcquiringDetailsRequest r) =>
        GetAsync(() => _repository.GetGlAsync(r), "acquiring GL");
    public Task<Result<PagedResponse<AcquiringFeTransaction>>> GetFeAsync(AcquiringDetailsRequest r) =>
        GetAsync(() => _repository.GetFeAsync(r), "acquiring FE");
    public Task<Result<PagedResponse<AcquiringEpTransaction>>> GetEpAsync(AcquiringDetailsRequest r) =>
        GetAsync(() => _repository.GetEpAsync(r), "acquiring EP");

    private static async Task<Result<PagedResponse<T>>> GetAsync<T>(Func<Task<PagedResponse<T>>> get, string name)
    {
        try { return Result<PagedResponse<T>>.Success(APIResponseCodes.SUCCESS_CODE, APIResponseMessages.SUCCESS_MSG, await get()); }
        catch (Exception ex) { return Result<PagedResponse<T>>.Failure(APIResponseCodes.ERROR_CODE, $"Failed to retrieve {name} details: {ex.Message}"); }
    }
}
