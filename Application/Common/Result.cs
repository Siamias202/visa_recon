namespace VISA_RECON.API.Application.Common
{
    public sealed class Result<T>
    {
        public bool IsSuccess { get; }
        public string Code { get; }
        public string Message { get; }
        public T? Value { get; }

        private Result(bool isSuccess, string code, string message, T? value)
        {
            IsSuccess = isSuccess;
            Code = code;
            Message = message;
            Value = value;
        }
        public static Result<Unit> Success(string code, string message)
        => Result<Unit>.Success(code, message, new Unit());

        public static Result<T> Success(string code, string message, T value)
            => new(true, code, message, value);

        public static Result<T> Failure(string code, string message)
            => new(false, code, message, default);
    }
}
