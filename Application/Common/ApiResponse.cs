namespace VISA_RECON.API.Application.Common
{
    public sealed class ApiResponse<T>
    {
        public string Code { get; }
        public string Message { get; }
        public T? Data { get; }

        public ApiResponse(string code, string message, T? data)
        {
            Code = code;
            Message = message;
            Data = data;
        }
    }
}
