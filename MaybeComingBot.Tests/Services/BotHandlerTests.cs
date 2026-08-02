using MaybeComingBot.Abstractions;
using MaybeComingBot.Constants;
using MaybeComingBot.Models;
using MaybeComingBot.Services;
using MaybeComingBot.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Xunit;

namespace MaybeComingBot.Tests.Services;

/// <summary>
/// Tests for <see cref="BotHandler"/> class.
/// </summary>
public class BotHandlerTests
{
    private const long ChatId = 100;
    private const long UserId = 200;

    private static BotHandler BuildHandler(
        out Mock<ITelegramSender> sender,
        out Mock<IScheduleStorage> storage,
        out Mock<IUserStateService> userState,
        int maxRequests = 5)
    {
        var senderMock = new Mock<ITelegramSender>();
        var storageMock = new Mock<IScheduleStorage>();
        var userStateMock = new Mock<IUserStateService>();
        var refreshMock = new Mock<IScheduleRefreshService>();

        var settings = Options.Create(new BotSettings
        {
            AdminUserIds = new[] { 1L },
            RateLimitMaxRequests = maxRequests,
            RateLimitWindowMinutes = 1
        });

        var formatter = new ScheduleFormatter(storageMock.Object, Mock.Of<ILogger<ScheduleFormatter>>());
        var guard = new StopSelectionGuard(
            storageMock.Object,
            userStateMock.Object,
            senderMock.Object,
            Mock.Of<ILogger<StopSelectionGuard>>());

        var dispatcher = new CommandDispatcher(
            storageMock.Object,
            userStateMock.Object,
            refreshMock.Object,
            formatter,
            guard,
            senderMock.Object,
            settings,
            Mock.Of<ILogger<CommandDispatcher>>());

        var rateLimiter = new RateLimiter(settings, Mock.Of<ILogger<RateLimiter>>());
        var handler = new BotHandler(
            senderMock.Object,
            userStateMock.Object,
            dispatcher,
            rateLimiter,
            Mock.Of<ILogger<BotHandler>>());

        sender = senderMock;
        storage = storageMock;
        userState = userStateMock;
        return handler;
    }

    private static Update CreateMessageUpdate(long chatId, long userId, string text, bool isBot = false)
        => new()
        {
            Message = new Message
            {
                Id = 1,
                Text = text,
                Chat = new Chat { Id = chatId },
                From = new User { Id = userId, FirstName = "Test", IsBot = isBot }
            }
        };

    private static Update CreateCallbackUpdate(long chatId, long userId, string data)
        => new()
        {
            CallbackQuery = new CallbackQuery
            {
                Id = "cb-1",
                From = new User { Id = userId, FirstName = "Test", IsBot = false },
                Data = data,
                Message = new Message { Id = 2, Chat = new Chat { Id = chatId } }
            }
        };

    private static Stop CreateStop(string id = "ST001", string name = "Central Station")
        => new()
        {
            Id = id,
            Name = name,
            Schedule = new List<DaySchedule>
            {
                new() { Day = 0, Trips = new List<Trip> { new("09:00", 10) } }
            }
        };

    // ============================================================
    // Message command routing
    // ============================================================

    [Fact]
    public async Task HandleUpdateAsync_SlashCommand_RoutesToDispatcher()
    {
        // Arrange
        var handler = BuildHandler(out var sender, out var storage, out _);
        storage.Setup(x => x.GetAllStops()).Returns(new List<Stop> { CreateStop() });

        // Act
        await handler.HandleUpdateAsync(CreateMessageUpdate(ChatId, UserId, "/start"));

        // Assert
        sender.Verify(
            x => x.SendAsync(ChatId, BotMessages.Welcome, It.IsAny<ParseMode?>(), It.IsAny<ReplyMarkup>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleUpdateAsync_CommandWithBotSuffix_StripsBotName()
    {
        // Arrange
        var handler = BuildHandler(out var sender, out var storage, out _);
        storage.Setup(x => x.GetAllStops()).Returns(new List<Stop> { CreateStop() });

        // Act
        await handler.HandleUpdateAsync(CreateMessageUpdate(ChatId, UserId, "/start@MyTestBot"));

        // Assert
        // The @MyTestBot suffix must be stripped so the /start command is dispatched.
        sender.Verify(
            x => x.SendAsync(ChatId, BotMessages.Welcome, It.IsAny<ParseMode?>(), It.IsAny<ReplyMarkup>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleUpdateAsync_PlainButtonText_RoutesAsCommand()
    {
        // Arrange
        var handler = BuildHandler(out var sender, out var storage, out var userState);

        var stop = CreateStop();
        storage.Setup(x => x.GetStop(stop.Id)).Returns(stop);
        storage.Setup(x => x.GetAllStops()).Returns(new List<Stop> { stop });

        userState
            .Setup(x => x.TryGet(UserId, out It.Ref<UserState?>.IsAny))
            .Returns((long id, out UserState? state) =>
            {
                state = new UserState { SelectedStopId = stop.Id };
                return true;
            });

        // No trips today => the formatter searches following days (deterministic output).
        for (int day = 0; day < 7; day++)
            storage.Setup(x => x.GetTripsForDay(stop.Id, day)).Returns(new List<Trip>());

        var tomorrow = ((int)DateTime.Now.DayOfWeek + 1) % 7;
        storage
            .Setup(x => x.GetTripsForDay(stop.Id, tomorrow))
            .Returns(new List<Trip> { new("09:00", 10) });

        // Act
        await handler.HandleUpdateAsync(CreateMessageUpdate(ChatId, UserId, "Nearest"));

        // Assert
        sender.Verify(
            x => x.SendAsync(ChatId, It.Is<string>(s => s.Contains("Next trip")), ParseMode.Markdown, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ============================================================
    // Message filtering
    // ============================================================

    [Fact]
    public async Task HandleUpdateAsync_EditedMessage_IsIgnored()
    {
        // Arrange
        var handler = BuildHandler(out var sender, out _, out _);

        var update = new Update
        {
            EditedMessage = new Message
            {
                Id = 1,
                Text = "/start",
                Chat = new Chat { Id = ChatId },
                From = new User { Id = UserId }
            }
        };

        // Act
        await handler.HandleUpdateAsync(update);

        // Assert
        sender.Verify(
            x => x.SendAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<ParseMode?>(), It.IsAny<ReplyMarkup?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleUpdateAsync_BotMessage_IsIgnored()
    {
        // Arrange
        var handler = BuildHandler(out var sender, out _, out _);

        // Act
        await handler.HandleUpdateAsync(CreateMessageUpdate(ChatId, UserId, "/start", isBot: true));

        // Assert
        sender.Verify(
            x => x.SendAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<ParseMode?>(), It.IsAny<ReplyMarkup?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleUpdateAsync_EmptyTextMessage_IsIgnored()
    {
        // Arrange
        var handler = BuildHandler(out var sender, out _, out _);

        // Act
        await handler.HandleUpdateAsync(CreateMessageUpdate(ChatId, UserId, "   "));

        // Assert
        sender.Verify(
            x => x.SendAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<ParseMode?>(), It.IsAny<ReplyMarkup?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleUpdateAsync_NonMessageUpdate_IsIgnored()
    {
        // Arrange
        var handler = BuildHandler(out var sender, out _, out _);
        var update = new Update(); // No payload => Type is not Message/CallbackQuery.

        // Act
        await handler.HandleUpdateAsync(update);

        // Assert
        sender.Verify(
            x => x.SendAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<ParseMode?>(), It.IsAny<ReplyMarkup?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ============================================================
    // Rate limiting
    // ============================================================

    [Fact]
    public async Task HandleUpdateAsync_RateLimited_SendsTooManyRequests()
    {
        // Arrange
        var handler = BuildHandler(out var sender, out var storage, out _, maxRequests: 1);
        storage.Setup(x => x.GetAllStops()).Returns(new List<Stop> { CreateStop() });

        // Act
        await handler.HandleUpdateAsync(CreateMessageUpdate(ChatId, UserId, "/start")); // allowed
        await handler.HandleUpdateAsync(CreateMessageUpdate(ChatId, UserId, "/help"));  // denied

        // Assert
        sender.Verify(
            x => x.SendAsync(ChatId, BotMessages.TooManyRequests, It.IsAny<ParseMode?>(), It.IsAny<ReplyMarkup?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ============================================================
    // Callback query routing
    // ============================================================

    [Fact]
    public async Task HandleUpdateAsync_CallbackQuery_RoutesToStopSelection()
    {
        // Arrange
        var handler = BuildHandler(out var sender, out var storage, out var userState);

        var stop = CreateStop();
        storage.Setup(x => x.GetStop(stop.Id)).Returns(stop);

        userState
            .Setup(x => x.TryGet(UserId, out It.Ref<UserState?>.IsAny))
            .Returns((long id, out UserState? state) =>
            {
                state = new UserState { SelectedStopId = string.Empty };
                return true;
            });

        // Act
        await handler.HandleUpdateAsync(CreateCallbackUpdate(ChatId, UserId, stop.Id));

        // Assert
        userState.Verify(x => x.SetStop(UserId, stop.Id), Times.Once);
        sender.Verify(
            x => x.SendAsync(ChatId, BotMessages.StopSelected(stop.Name), ParseMode.Markdown, It.IsAny<ReplyMarkup>(), It.IsAny<CancellationToken>()),
            Times.Once);
        sender.Verify(x => x.AnswerCallbackAsync("cb-1", null, false, It.IsAny<CancellationToken>()), Times.Once);
    }
}

