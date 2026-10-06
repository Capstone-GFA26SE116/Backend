using System.Net;
using System.Net.Http.Json;
using METANOIA.Application.Dto;
using METANOIA.Test.Infrastructure;
using Xunit;

namespace METANOIA.Test
{
    public class GoogleCalendarTests
    {
        [Fact]
        public async Task Status_WithoutToken_Returns401()
        {
            using var factory = new ApiFactory();
            var client = factory.CreateClient();

            var response = await client.GetAsync("/api/google-calendar/status");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Status_BeforeConnecting_ReportsNotConnected()
        {
            using var factory = new ApiFactory();
            var (client, _) = await TestHelpers.LoginAsync(factory);

            var status = await client.GetFromJsonAsync<GoogleCalendarStatusDto>("/api/google-calendar/status");

            Assert.False(status!.IsConnected);
        }

        [Fact]
        public async Task Events_BeforeConnecting_Returns409WithConnectUrl()
        {
            using var factory = new ApiFactory();
            var (client, _) = await TestHelpers.LoginAsync(factory);

            var response = await client.GetAsync("/api/google-calendar/events?from=2026-10-01T00:00:00Z&to=2026-10-31T00:00:00Z");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("GOOGLE_CALENDAR_NOT_CONNECTED", body);
            Assert.Contains("/api/google-calendar/connect", body);
        }

        [Fact]
        public async Task Connect_ReturnsAuthorizationUrlWithCalendarScopeAndState()
        {
            using var factory = new ApiFactory();
            var (client, _) = await TestHelpers.LoginAsync(factory);

            var result = await client.GetFromJsonAsync<Dictionary<string, string>>("/api/google-calendar/connect");
            var url = result!["authorizationUrl"];

            Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth", url);
            Assert.Contains("scope=https%3A%2F%2Fwww.googleapis.com%2Fauth%2Fcalendar.events", url);
            Assert.Contains("access_type=offline", url);
            Assert.Contains("state=", url);
        }

        [Fact]
        public async Task Callback_WhenUserDenies_RedirectsToFrontendWithDenied()
        {
            using var factory = new ApiFactory();
            var client = factory.CreateClient(new() { AllowAutoRedirect = false });

            var response = await client.GetAsync("/api/google-calendar/callback?error=access_denied");

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            var location = response.Headers.Location!.ToString();
            Assert.StartsWith(ApiFactory.CalendarRedirectUri, location);
            Assert.Contains("calendar=denied", location);
        }

        [Fact]
        public async Task Callback_WithTamperedState_RedirectsToFrontendWithError()
        {
            using var factory = new ApiFactory();
            var client = factory.CreateClient(new() { AllowAutoRedirect = false });

            var response = await client.GetAsync("/api/google-calendar/callback?code=abc&state=tampered");

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            var location = response.Headers.Location!.ToString();
            Assert.Contains("calendar=error", location);
            Assert.Contains("message=", location);
        }

        [Fact]
        public async Task Cors_PreflightFromAllowedFrontend_IsAccepted()
        {
            using var factory = new ApiFactory();
            var client = factory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Options, "/api/projects");
            request.Headers.Add("Origin", ApiFactory.FrontendOrigin);
            request.Headers.Add("Access-Control-Request-Method", "GET");
            request.Headers.Add("Access-Control-Request-Headers", "authorization");

            var response = await client.SendAsync(request);

            Assert.Equal(ApiFactory.FrontendOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        }
    }
}
