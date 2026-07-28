namespace VISA_RECON.API.Application.Exceptions

{
    public class UnauthorizedException : Exception
    {
        public UnauthorizedException(string message) : base(message) { }
    }

    public class TooManyAttemptsException : Exception
    {
        public TooManyAttemptsException(string message) : base(message) { }
    }
    public class BadRequestException : Exception
    {
        public BadRequestException(string message) : base(message) { }
    }
}

