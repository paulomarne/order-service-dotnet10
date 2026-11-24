namespace OrderService.Application.Abstractions;

public sealed class NotFoundException : Exception
{
    public NotFoundException(string resource, Guid id)
        : base($"{DisplayName(resource)} '{id}' não foi encontrado.")
    {
        Resource = resource;
        Id = id;
        Code = $"{resource}_not_found";
    }

    public string Resource { get; }

    public Guid Id { get; }

    public string Code { get; }

    private static string DisplayName(string resource) => resource switch
    {
        "order" => "Pedido",
        "product" => "Produto",
        _ => resource
    };
}

public sealed class ApplicationValidationException : Exception
{
    public ApplicationValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("Um ou mais campos falharam na validação.")
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

public sealed class InvalidCredentialsException()
    : Exception("Credenciais inválidas.");

public sealed class ConcurrencyException(string message, Exception? innerException = null)
    : Exception(message, innerException);
