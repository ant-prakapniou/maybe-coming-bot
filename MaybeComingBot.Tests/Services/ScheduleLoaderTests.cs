using MaybeComingBot.Models;
using MaybeComingBot.Services;
using MaybeComingBot.Utils;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace MaybeComingBot.Tests.Services;

public class ScheduleLoaderTests
{
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ILogger<ScheduleLoader>> _loggerMock;
    private readonly ScheduleLoader _loader;

    public ScheduleLoaderTests()
    {
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _loggerMock = new Mock<ILogger<ScheduleLoader>>();
        _loader = new ScheduleLoader(_httpClientFactoryMock.Object, _loggerMock.Object);
    }

    // ============================================================
    // LoadAsync Tests (no URL parameter)
    // ============================================================

    [Fact]
    public async Task LoadAsync_ValidResponse_ReturnsSchedule()
    {
        // Arrange
        var json = CreateValidJson();
        var httpClient = CreateHttpClientWithResponse(HttpStatusCode.OK, json);
        _httpClientFactoryMock
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        // Act
        var result = await _loader.LoadAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.Stops.Should().HaveCount(2);
        result.Stops[0].Id.Should().Be("ST001");
        result.Stops[0].Name.Should().Be("Central Station");
        result.Stops[1].Id.Should().Be("ST002");
        result.Stops[1].Name.Should().Be("Main Square");
    }

    [Fact]
    public async Task LoadAsync_EmptyJson_ReturnsEmptySchedule()
    {
        // Arrange
        var httpClient = CreateHttpClientWithResponse(HttpStatusCode.OK, "");
        _httpClientFactoryMock
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        // Act
        var result = await _loader.LoadAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.Stops.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_WhitespaceJson_ReturnsEmptySchedule()
    {
        // Arrange
        var httpClient = CreateHttpClientWithResponse(HttpStatusCode.OK, "   ");
        _httpClientFactoryMock
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        // Act
        var result = await _loader.LoadAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.Stops.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_EmptyContentLength_ReturnsEmptySchedule()
    {
        // Arrange
        var httpClient = CreateHttpClientWithResponse(HttpStatusCode.OK, "", 0);
        _httpClientFactoryMock
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        // Act
        var result = await _loader.LoadAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.Stops.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_InvalidJson_ThrowsInvalidOperationException()
    {
        // Arrange
        var httpClient = CreateHttpClientWithResponse(HttpStatusCode.OK, "invalid json");
        _httpClientFactoryMock
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        // Act
        var act = async () => await _loader.LoadAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Failed to parse JSON*");
    }

    [Fact]
    public async Task LoadAsync_HttpRequestException_Throws()
    {
        // Arrange
        var httpClient = CreateHttpClientThrowing(new HttpRequestException("Network error"));
        _httpClientFactoryMock
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        // Act
        var act = async () => await _loader.LoadAsync();

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("Network error");
    }

    [Fact]
    public async Task LoadAsync_NoBaseAddress_ThrowsInvalidOperationException()
    {
        // Arrange
        var httpClient = CreateHttpClientWithoutBaseAddress();
        _httpClientFactoryMock
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        // Act
        var act = async () => await _loader.LoadAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("ScheduleLoader HttpClient BaseAddress is not configured or empty.*");
    }

    [Fact]
    public async Task LoadAsync_Cancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var httpClient = CreateHttpClientWithResponse(HttpStatusCode.OK, CreateValidJson());
        _httpClientFactoryMock
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        // Act
        var act = async () => await _loader.LoadAsync(cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ============================================================
    // LoadAsync Tests (with URL parameter)
    // ============================================================

    [Fact]
    public async Task LoadAsync_WithCustomUrl_LoadsFromUrl()
    {
        // Arrange
        var json = CreateValidJson();
        var httpClient = CreateHttpClientWithResponse(HttpStatusCode.OK, json);
        _httpClientFactoryMock
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        // Act
        var result = await _loader.LoadAsync("https://custom.url/schedule.json", TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.Stops.Should().HaveCount(2);
    }

    [Fact]
    public async Task LoadAsync_WithNullUrl_UsesDefault()
    {
        // Arrange
        var json = CreateValidJson();
        var httpClient = CreateHttpClientWithResponse(HttpStatusCode.OK, json);
        _httpClientFactoryMock
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        // Act
        var result = await _loader.LoadAsync(null, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.Stops.Should().HaveCount(2);
    }

    [Fact]
    public async Task LoadAsync_WithEmptyUrl_UsesDefault()
    {
        // Arrange
        var json = CreateValidJson();
        var httpClient = CreateHttpClientWithResponse(HttpStatusCode.OK, json);
        _httpClientFactoryMock
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        // Act
        var result = await _loader.LoadAsync("", TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.Stops.Should().HaveCount(2);
    }

    [Fact]
    public async Task LoadAsync_WithInvalidCustomUrl_Throws()
    {
        // Arrange
        var httpClient = CreateHttpClientThrowing(new HttpRequestException("Not found"));
        _httpClientFactoryMock
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        // Act
        var act = async () => await _loader.LoadAsync("https://invalid.url");

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    // ============================================================
    // Helper Methods
    // ============================================================

    private static HttpClient CreateHttpClientWithResponse(HttpStatusCode statusCode, string content, long? contentLength = null)
    {
        var handlerMock = new Mock<HttpMessageHandler>();

        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                var response = new HttpResponseMessage(statusCode);
                var contentBytes = Encoding.UTF8.GetBytes(content);
                var stream = new MemoryStream(contentBytes);
                var streamContent = new StreamContent(stream);
                streamContent.Headers.ContentLength = contentLength ?? contentBytes.Length;
                response.Content = streamContent;
                return response;
            });

        var client = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://test.api/")
        };

        return client;
    }

    private static HttpClient CreateHttpClientWithoutBaseAddress()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        var client = new HttpClient(handlerMock.Object);
        return client;
    }

    private static HttpClient CreateHttpClientThrowing(Exception exception)
    {
        var handlerMock = new Mock<HttpMessageHandler>();

        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(exception);

        var client = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://test.api/")
        };

        return client;
    }

    private static string CreateValidJson()
    {
        var root = new ScheduleRoot
        {
            Stops = new List<Stop>
            {
                new Stop
                {
                    Id = "ST001",
                    Name = "Central Station",
                    Schedule = new List<DaySchedule>
                    {
                        new DaySchedule
                        {
                            Day = 0,
                            Trips = new List<Trip>
                            {
                                new Trip { Time = "08:00", Bus = 10 },
                                new Trip { Time = "09:00", Bus = 20 }
                            }
                        }
                    }
                },
                new Stop
                {
                    Id = "ST002",
                    Name = "Main Square",
                    Schedule = new List<DaySchedule>()
                }
            }
        };

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        return JsonSerializer.Serialize(root, options);
    }
}