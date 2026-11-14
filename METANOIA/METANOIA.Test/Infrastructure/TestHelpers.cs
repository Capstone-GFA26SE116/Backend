using System.Net.Http.Headers;
using System.Net.Http.Json;
using METANOIA.Application.Dto;

namespace METANOIA.Test.Infrastructure
{
    public static class TestHelpers
    {
        public static async Task<(HttpClient Client, AuthResponseDto Auth)> LoginAsync(
            ApiFactory factory,
            string subject = "user-1")
        {
            await factory.EnsureDefaultRoleAsync();

            var client = factory.CreateClient();
            var response = await client.PostAsJsonAsync("/api/auth/google", new GoogleLoginRequestDto
            {
                IdToken = $"valid:{subject}:{subject}@test.com:Test {subject}"
            });
            response.EnsureSuccessStatusCode();

            var auth = (await response.Content.ReadFromJsonAsync<AuthResponseDto>())!;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
            // Thao tác nhạy cảm của Admin bắt buộc có lý do; test mặc định gửi sẵn (test về việc thiếu lý do tự bỏ header này)
            client.DefaultRequestHeaders.Add("X-Admin-Reason", "ly-do-kiem-thu");
            return (client, auth);
        }
    }
}
