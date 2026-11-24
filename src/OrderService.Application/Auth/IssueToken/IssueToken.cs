using FluentValidation;
using OrderService.Application.Abstractions;
using OrderService.Application.Common;

namespace OrderService.Application.Auth.IssueToken;

public sealed record IssueTokenCommand
{
    public string ClientId { get; init; } = string.Empty;

    public string ClientSecret { get; init; } = string.Empty;
}

public sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn);

public sealed class IssueTokenValidator : AbstractValidator<IssueTokenCommand>
{
    public IssueTokenValidator()
    {
        RuleFor(command => command.ClientId)
            .NotEmpty()
            .WithMessage("O clientId é obrigatório.");

        RuleFor(command => command.ClientSecret)
            .NotEmpty()
            .WithMessage("O clientSecret é obrigatório.");
    }
}

public sealed class IssueTokenHandler(
    IValidator<IssueTokenCommand> validator,
    IClientCredentialValidator credentials,
    ITokenIssuer tokens,
    IClock clock)
{
    public async Task<TokenResponse> HandleAsync(IssueTokenCommand command, CancellationToken cancellationToken)
    {
        await validator.ThrowIfInvalidAsync(command, cancellationToken);

        if (!credentials.Validate(command.ClientId, command.ClientSecret))
        {
            throw new InvalidCredentialsException();
        }

        var issued = tokens.Issue(command.ClientId.Trim(), clock.UtcNow);
        return new TokenResponse(issued.Value, "Bearer", issued.ExpiresInSeconds);
    }
}
