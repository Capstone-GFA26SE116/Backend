using System.Net;
using System.Net.Http.Json;
using METANOIA.Application.Dto;
using METANOIA.Test.Infrastructure;
using Xunit;

namespace METANOIA.Test
{
    public class ClientTests
    {
        private static ClientRequestDto NewClient(string name = "Acme Corp") => new()
        {
            Name = name,
            ContactEmail = "contact@acme.test",
            Phone = "0900000000",
            Notes = "VIP"
        };

        [Fact]
        public async Task CreateClient_ThenGet_ReturnsSameClient()
        {
            using var factory = new ApiFactory();
            var (client, _) = await TestHelpers.LoginAsync(factory);

            var created = await client.PostAsJsonAsync("/api/clients", NewClient());
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var dto = (await created.Content.ReadFromJsonAsync<ClientDto>())!;

            var fetched = await client.GetFromJsonAsync<ClientDto>($"/api/clients/{dto.Id}");
            Assert.Equal("Acme Corp", fetched!.Name);
            Assert.Equal("contact@acme.test", fetched.ContactEmail);
        }

        [Fact]
        public async Task CreateClient_WithEmptyName_Returns400()
        {
            using var factory = new ApiFactory();
            var (client, _) = await TestHelpers.LoginAsync(factory);

            var response = await client.PostAsJsonAsync("/api/clients", NewClient(name: " "));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task CreateClient_WithInvalidEmail_Returns400()
        {
            using var factory = new ApiFactory();
            var (client, _) = await TestHelpers.LoginAsync(factory);
            var request = NewClient();
            request.ContactEmail = "not-an-email";

            var response = await client.PostAsJsonAsync("/api/clients", request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task UpdateClient_ChangesFields()
        {
            using var factory = new ApiFactory();
            var (client, _) = await TestHelpers.LoginAsync(factory);
            var created = await (await client.PostAsJsonAsync("/api/clients", NewClient())).Content.ReadFromJsonAsync<ClientDto>();

            var response = await client.PutAsJsonAsync($"/api/clients/{created!.Id}", NewClient(name: "Acme Renamed"));
            var updated = await response.Content.ReadFromJsonAsync<ClientDto>();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Acme Renamed", updated!.Name);
        }

        [Fact]
        public async Task DeleteClient_ThenGet_Returns404()
        {
            using var factory = new ApiFactory();
            var (client, _) = await TestHelpers.LoginAsync(factory);
            var created = await (await client.PostAsJsonAsync("/api/clients", NewClient())).Content.ReadFromJsonAsync<ClientDto>();

            var delete = await client.DeleteAsync($"/api/clients/{created!.Id}");
            var fetch = await client.GetAsync($"/api/clients/{created.Id}");

            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, fetch.StatusCode);
        }

        [Fact]
        public async Task OtherUsersClient_Returns404()
        {
            using var factory = new ApiFactory();
            var (owner, _) = await TestHelpers.LoginAsync(factory, "owner");
            var created = await (await owner.PostAsJsonAsync("/api/clients", NewClient())).Content.ReadFromJsonAsync<ClientDto>();

            var (stranger, _) = await TestHelpers.LoginAsync(factory, "stranger");
            var response = await stranger.GetAsync($"/api/clients/{created!.Id}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}
