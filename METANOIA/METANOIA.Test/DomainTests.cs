using System.Net;
using System.Net.Http.Json;
using METANOIA.Application.Dto;
using METANOIA.Test.Infrastructure;
using Xunit;

namespace METANOIA.Test
{
    public class DomainTests
    {
        [Fact]
        public async Task Admin_CanCreateDomain_AndAnyUserCanList()
        {
            using var factory = new ApiFactory();
            await factory.SeedUserWithRoleAsync("admin-1", "Admin");
            var (admin, _) = await TestHelpers.LoginAsync(factory, "admin-1");
            var (user, _) = await TestHelpers.LoginAsync(factory, "user-1");

            var created = await admin.PostAsJsonAsync("/api/domains", new DomainRequestDto { Name = "Design" });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

            var list = await user.GetFromJsonAsync<List<DomainDto>>("/api/domains");
            Assert.Contains(list!, d => d.Name == "Design" && d.IsActive);
        }

        [Fact]
        public async Task Freelancer_CannotCreateDomain_Returns403()
        {
            using var factory = new ApiFactory();
            var (user, _) = await TestHelpers.LoginAsync(factory, "user-1");

            var response = await user.PostAsJsonAsync("/api/domains", new DomainRequestDto { Name = "Design" });

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task DuplicateName_IgnoringCase_Returns400()
        {
            using var factory = new ApiFactory();
            await factory.SeedUserWithRoleAsync("admin-1", "Admin");
            var (admin, _) = await TestHelpers.LoginAsync(factory, "admin-1");
            await admin.PostAsJsonAsync("/api/domains", new DomainRequestDto { Name = "Design" });

            var response = await admin.PostAsJsonAsync("/api/domains", new DomainRequestDto { Name = "DESIGN" });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Admin_CanDeactivateDomain_ViaUpdate()
        {
            using var factory = new ApiFactory();
            await factory.SeedUserWithRoleAsync("admin-1", "Admin");
            var (admin, _) = await TestHelpers.LoginAsync(factory, "admin-1");
            var created = await (await admin.PostAsJsonAsync("/api/domains", new DomainRequestDto { Name = "Writing" }))
                .Content.ReadFromJsonAsync<DomainDto>();

            var response = await admin.PutAsJsonAsync($"/api/domains/{created!.Id}",
                new DomainRequestDto { Name = "Writing", IsActive = false });
            var updated = await response.Content.ReadFromJsonAsync<DomainDto>();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(updated!.IsActive);
        }
    }
}
