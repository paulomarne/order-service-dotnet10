namespace OrderService.Domain.Exceptions;

public abstract class DomainException : Exception
{
    protected DomainException(string code, string message) : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

public sealed class DomainRuleException(string code, string message) : DomainException(code, message);
