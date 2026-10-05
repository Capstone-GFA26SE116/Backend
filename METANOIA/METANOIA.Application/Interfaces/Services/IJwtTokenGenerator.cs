using METANOIA.Domain.Entities;

namespace METANOIA.Application.Interfaces.Services
{
    public interface IJwtTokenGenerator
    {
        (string Token, DateTime ExpiresAtUtc) GenerateToken(User user);
    }
}
