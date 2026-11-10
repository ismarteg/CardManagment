using IdCard.Domain.Enums;

namespace IdCard.Api.Dtos;

public record LoginRequest(string UserName, string Password);
public record LoginResponse(string Token, Guid UserId, string UserName, string DisplayName, string Role);
public record CreateUserRequest(string UserName, string Email, string Password, string DisplayName, UserRole Role);
