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
/// Tests for <see cref="CommandDispatcher"/> class.
/// </summary>
public class CommandDispatcherTests
{
private const long AdminId = 999888777;
    private const long UserId = 200;
    private const long ChatId = 100;

    private static CommandDispatcher BuildDispatcher(
        out Mock<ITelegramSender> sender,
        out Mock<IScheduleStorage> storage,
        out Mock<IUserStateService> userState,
        out Mock<IScheduleRefreshService> refresh,
        string? supportUsername = null)
    {
        var senderMock = new Mock<ITelegramSender>();
        var storageMock = new Mock<IScheduleStorage>();
        var userStateMock = new Mock<IUserStateService>();
        var refreshMock = new Mock<IScheduleRefreshService>();

        var settings = Options.Create(new BotSettings
        {
            AdminUserIds = new[] { AdminId },
            SupportUsername = supportUsername ?? string.Empty,
            ScheduleUrl = "http://localhost/schedule.json",
            BotToken = "1234567890:ABC"
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

        sender = senderMock;
        storage = storageMock;
        userState = userStateMock;
        refresh = refreshMock;
        return dispatcher;
    }

    private static void SetupUserWithStop(Mock<IUserStateService> userState, string stopId)
    {
        userState
            .Setup(x => x.TryGet(UserId, out It.Ref<UserState?>.IsAny))
            .Returns((long id, out UserState? state) =>
            {
                state = new UserState { SelectedStopId = stopId };
                return true;
            });
    }

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

    private static CallbackQuery CreateCallback(long chatId, long userId, string data)
        => new()
        {
            Id = "cb-1",
            From = new User { Id = userId, FirstName = "Test" },
            Data = data,
            Message = new Message { Id = 2, Chat = new Chat { Id = chatId } }
        };

    // ============================================================
    // /cancel
    // ============================================================

    [Fact]
    public async Task DispatchCancel_ClearsStateAndConfirms()
    {
        var dispatcher = BuildDispatcher(out var sender, out _, out var userState, out _);

        await dispatcher.DispatchCommandAsync(ChatId, UserId, BotCommands.Cancel, null);

        userState.Verify(x => x.Clear(UserId), Times.Once);
        sender.Verify(
            x => x.SendAsync(ChatId, BotMessages.CancelConfirmed, It.IsAny<ParseMode?>(), It.IsAny<ReplyMarkup?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ============================================================
    // /start
    // ============================================================

    [Fact]
    public async Task DispatchStart_WithStops_ShowsWelcome()
    {
        var dispatcher = BuildDispatcher(out var sender, out var storage, out _, out _);
        storage.Setup(x => x.GetAllStops()).Returns(new List<Stop> { CreateStop() });

        await dispatcher.DispatchCommandAsync(ChatId, UserId, BotCommands.Start, null);

        sender.Verify(
            x => x.SendAsync(ChatId, BotMessages.Welcome, It.IsAny<ParseMode?>(), It.IsAny<ReplyMarkup>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DispatchStart_NoStops_AdminSeesRefreshButton()
    {
        var dispatcher = BuildDispatcher(out var sender, out var storage, out _, out _);
        storage.Setup(x => x.GetAllStops()).Returns(new List<Stop>());

        await dispatcher.DispatchCommandAsync(ChatId, AdminId, BotCommands.Start, null);

        sender.Verify(
            x => x.SendAsync(ChatId, BotMessages.ScheduleNotLoaded, It.IsAny<ParseMode?>(), It.IsAny<ReplyMarkup>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DispatchStart_NoStops_NonAdminSeesNoSchedule()
    {
        var dispatcher = BuildDispatcher(out var sender, out var storage, out _, out _);
        storage.Setup(x => x.GetAllStops()).Returns(new List<Stop>());

        await dispatcher.DispatchCommandAsync(ChatId, UserId, BotCommands.Start, null);

        sender.Verify(
            x => x.SendAsync(ChatId, BotMessages.NoSchedule, It.IsAny<ParseMode?>(), It.IsAny<ReplyMarkup?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ============================================================
    // /help
    // ============================================================

    [Fact]
    public async Task DispatchHelp_SendsHelpMessage()
    {
        var dispatcher = BuildDispatcher(out var sender, out _, out _, out _);

        await dispatcher.DispatchCommandAsync(ChatId, UserId, BotCommands.Help, null);

        sender.Verify(
            x => x.SendAsync(ChatId, BotMessages.Help(), ParseMode.Markdown, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DispatchHelp_WithSupportUsername_IncludesAdminContactLink()
    {
        var dispatcher = BuildDispatcher(out var sender, out _, out _, out _, supportUsername: "support_admin");

        await dispatcher.DispatchCommandAsync(ChatId, UserId, BotCommands.Help, null);

        sender.Verify(
            x => x.SendAsync(
                ChatId,
                It.Is<string>(s =>
                    s.Contains("Help & commands") &&
                    s.Contains("[@support_admin](https://t.me/support_admin)") &&
                    !s.Contains("tg://user")),
                ParseMode.Markdown,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ============================================================
    // /update (admin)
    // ============================================================

    [Fact]
    public async Task DispatchUpdate_AsAdmin_RefreshesAndShowsStops()
    {
        var dispatcher = BuildDispatcher(out var sender, out var storage, out _, out var refresh);
        refresh.Setup(x => x.ForceRefreshAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        storage.Setup(x => x.GetAllStops()).Returns(new List<Stop> { CreateStop() });

        await dispatcher.DispatchCommandAsync(ChatId, AdminId, BotCommands.Update, null);

        refresh.Verify(x => x.ForceRefreshAsync(It.IsAny<CancellationToken>()), Times.Once);
        sender.Verify(
            x => x.SendAsync(ChatId, It.Is<string>(s => s.Contains("Schedule updated successfully")), It.IsAny<ParseMode?>(), It.IsAny<ReplyMarkup>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DispatchUpdate_NonAdmin_Denied()
    {
        var dispatcher = BuildDispatcher(out var sender, out _, out _, out _);

        await dispatcher.DispatchCommandAsync(ChatId, UserId, BotCommands.Update, null);

        sender.Verify(
            x => x.SendAsync(ChatId, BotMessages.NoRights, It.IsAny<ParseMode?>(), It.IsAny<ReplyMarkup?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ============================================================
    // /status (admin)
    // ============================================================

    [Fact]
    public async Task DispatchStatus_AsAdmin_SendsStatus()
    {
        var dispatcher = BuildDispatcher(out var sender, out var storage, out var userState, out var refresh);
        refresh.Setup(x => x.GetStatus()).Returns("✅ LAST: 12:00:00 (5m ago)");
        refresh.Setup(x => x.GetLastAttemptInfo()).Returns("ATTEMPT: 12:00:00 (5m ago), FAILS: 0");
        storage.SetupGet(x => x.StopCount).Returns(10);
        userState.Setup(x => x.GetActiveUsersCount()).Returns(3);

        await dispatcher.DispatchCommandAsync(ChatId, AdminId, BotCommands.Status, null);

        sender.Verify(
            x => x.SendAsync(ChatId, It.Is<string>(s =>
                s.Contains("Bot Status") && s.Contains("10") && s.Contains("3")),
                ParseMode.Markdown, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ============================================================
    // /report
    // ============================================================

    [Fact]
    public async Task DispatchReport_WithoutText_ShowsInstructions()
    {
        var dispatcher = BuildDispatcher(out var sender, out _, out _, out _);

        await dispatcher.DispatchCommandAsync(ChatId, UserId, BotCommands.Report, null);

        sender.Verify(
            x => x.SendAsync(ChatId, It.Is<string>(s => s.Contains("Report a bug or issue")), It.IsAny<ParseMode?>(), null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DispatchReport_WithText_ForwardsToAdmins()
    {
        var dispatcher = BuildDispatcher(out var sender, out _, out var userState, out _);
        userState
            .Setup(x => x.GetOrCreate(UserId))
            .Returns(new UserState { FirstName = "John", LastName = "Doe", Username = "johndoe" });

        await dispatcher.DispatchCommandAsync(ChatId, UserId, BotCommands.Report, "Bus is late");

        sender.Verify(
            x => x.SendAsync(AdminId, It.Is<string>(s => s.Contains("NEW REPORT") && s.Contains("Bus is late")), ParseMode.Markdown, null, It.IsAny<CancellationToken>()),
            Times.Once);
        sender.Verify(
            x => x.SendAsync(ChatId, BotMessages.ReportSent, It.IsAny<ParseMode?>(), It.IsAny<ReplyMarkup?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DispatchReport_WithText_UsernameIsNotEscaped_KeepsMentionLink()
    {
        var dispatcher = BuildDispatcher(out var sender, out _, out var userState, out _);
        userState
            .Setup(x => x.GetOrCreate(UserId))
            .Returns(new UserState { FirstName = "Shadow", LastName = "Citizen", Username = "shadow_citizen" });

        await dispatcher.DispatchCommandAsync(ChatId, UserId, BotCommands.Report, "Bus 42 late");

        // The username must be forwarded as a raw "@shadow_citizen" (no escaped underscore)
        // so Telegram renders it as a tappable mention.
        sender.Verify(
            x => x.SendAsync(
                AdminId,
                It.Is<string>(s =>
                    s.Contains("USERNAME: @shadow_citizen") &&
                    !s.Contains("@shadow\\_citizen")),
                ParseMode.Markdown,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ============================================================
    // /report — privacy (admin ID must never be exposed)
    // ============================================================

    [Fact]
    public async Task DispatchReport_WithoutText_NeverExposesAdminId()
    {
        var dispatcher = BuildDispatcher(out var sender, out _, out _, out _);

        await dispatcher.DispatchCommandAsync(ChatId, UserId, BotCommands.Report, null);

        sender.Verify(
            x => x.SendAsync(
                ChatId,
                It.Is<string>(s =>
                    !s.Contains("tg://user") &&
                    !s.Contains(AdminId.ToString())),
                It.IsAny<ParseMode?>(),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DispatchReport_WithoutText_WithSupportUsername_RendersTmeLink()
    {
        var dispatcher = BuildDispatcher(out var sender, out _, out _, out _, supportUsername: "support_admin");

        await dispatcher.DispatchCommandAsync(ChatId, UserId, BotCommands.Report, null);

        sender.Verify(
            x => x.SendAsync(
                ChatId,
                It.Is<string>(s =>
                    s.Contains("https://t.me/support_admin") &&
                    s.Contains("[👤 @support_admin](https://t.me/support_admin)") &&
                    !s.Contains("tg://user") &&
                    !s.Contains("Contact @")),
                It.IsAny<ParseMode?>(),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DispatchReport_WithoutText_NoSupportUsername_NoContactLink()
    {
        var dispatcher = BuildDispatcher(out var sender, out _, out _, out _);

        await dispatcher.DispatchCommandAsync(ChatId, UserId, BotCommands.Report, null);

        sender.Verify(
            x => x.SendAsync(
                ChatId,
                It.Is<string>(s =>
                    !s.Contains("t.me/") &&
                    !s.Contains("tg://user")),
                It.IsAny<ParseMode?>(),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ============================================================
    // Emoji button label normalization
    // ============================================================

    [Fact]
    public async Task DispatchNearest_WithEmojiLabel_RoutesToNearest()
    {
        var dispatcher = BuildDispatcher(out var sender, out var storage, out var userState, out _);

        var stop = CreateStop();
        storage.Setup(x => x.GetStop(stop.Id)).Returns(stop);
        SetupUserWithStop(userState, stop.Id);

        // No trips for every day so the formatter produces a deterministic "no trips" output.
        for (int day = 0; day < 7; day++)
            storage.Setup(x => x.GetTripsForDay(stop.Id, day)).Returns(new List<Trip>());

        await dispatcher.DispatchCommandAsync(ChatId, UserId, BotCommands.NearestLabel, null);

        sender.Verify(
            x => x.SendAsync(ChatId, It.IsAny<string>(), ParseMode.Markdown, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DispatchReportBug_WithEmojiLabel_ShowsInstructions()
    {
        var dispatcher = BuildDispatcher(out var sender, out _, out _, out _);

        await dispatcher.DispatchCommandAsync(ChatId, UserId, BotCommands.ReportToAdminLabel, null);

        sender.Verify(
            x => x.SendAsync(ChatId, It.Is<string>(s => s.Contains("Report a bug or issue")), It.IsAny<ParseMode?>(), null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DispatchBack_WithEmojiLabel_ReturnsToStart()
    {
        var dispatcher = BuildDispatcher(out var sender, out var storage, out _, out _);
        storage.Setup(x => x.GetAllStops()).Returns(new List<Stop> { CreateStop() });

        await dispatcher.DispatchCommandAsync(ChatId, UserId, BotCommands.BackLabel, null);

        sender.Verify(
            x => x.SendAsync(ChatId, BotMessages.Welcome, It.IsAny<ParseMode?>(), It.IsAny<ReplyMarkup>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ============================================================
    // Unknown command
    // ============================================================

    [Fact]
    public async Task DispatchUnknownCommand_SendsUnknownMessage()
    {
        var dispatcher = BuildDispatcher(out var sender, out var storage, out var userState, out _);
        var stop = CreateStop();
        storage.Setup(x => x.GetStop(stop.Id)).Returns(stop);
        SetupUserWithStop(userState, stop.Id);

        await dispatcher.DispatchCommandAsync(ChatId, UserId, "bogus-command", null);

        sender.Verify(
            x => x.SendAsync(ChatId, BotMessages.UnknownCommand, It.IsAny<ParseMode?>(), It.IsAny<ReplyMarkup?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ============================================================
    // Command requiring stop selection
    // ============================================================

    [Fact]
    public async Task DispatchNearest_WithoutStop_ShowsChooseStopFirst()
    {
        var dispatcher = BuildDispatcher(out var sender, out _, out var userState, out _);
        userState
            .Setup(x => x.TryGet(UserId, out It.Ref<UserState?>.IsAny))
            .Returns((long id, out UserState? state) =>
            {
                state = new UserState { SelectedStopId = string.Empty };
                return true;
            });

        await dispatcher.DispatchCommandAsync(ChatId, UserId, BotCommands.Nearest, null);

        sender.Verify(
            x => x.SendAsync(ChatId, BotMessages.ChooseStopFirst, It.IsAny<ParseMode?>(), It.IsAny<ReplyMarkup?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DispatchToday_WithValidStop_SendsFormattedSchedule()
    {
        var dispatcher = BuildDispatcher(out var sender, out var storage, out var userState, out _);

        var stop = CreateStop();
        storage.Setup(x => x.GetStop(stop.Id)).Returns(stop);
        SetupUserWithStop(userState, stop.Id);

        // No trips for every day so the formatter produces a deterministic "no trips" output.
        for (int day = 0; day < 7; day++)
            storage.Setup(x => x.GetTripsForDay(stop.Id, day)).Returns(new List<Trip>());

        await dispatcher.DispatchCommandAsync(ChatId, UserId, BotCommands.Today, null);

        sender.Verify(
            x => x.SendAsync(ChatId, It.IsAny<string>(), ParseMode.Markdown, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ============================================================
    // HandleCallbackAsync
    // ============================================================

    [Fact]
    public async Task HandleCallback_ValidStop_SetsStopAndSendsKeyboard()
    {
        var dispatcher = BuildDispatcher(out var sender, out var storage, out var userState, out _);

        var stop = CreateStop();
        storage.Setup(x => x.GetStop(stop.Id)).Returns(stop);
        SetupUserWithStop(userState, string.Empty);

        await dispatcher.HandleCallbackAsync(CreateCallback(ChatId, UserId, stop.Id));

        userState.Verify(x => x.SetStop(UserId, stop.Id), Times.Once);
        sender.Verify(
            x => x.SendAsync(ChatId, BotMessages.StopSelected(stop.Name), ParseMode.Markdown, It.IsAny<ReplyMarkup>(), It.IsAny<CancellationToken>()),
            Times.Once);
        sender.Verify(x => x.AnswerCallbackAsync("cb-1", null, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleCallback_NoSession_SendsSessionExpired()
    {
        var dispatcher = BuildDispatcher(out var sender, out _, out var userState, out _);
        userState
            .Setup(x => x.TryGet(UserId, out It.Ref<UserState?>.IsAny))
            .Returns((long id, out UserState? state) =>
            {
                state = null;
                return false;
            });

        await dispatcher.HandleCallbackAsync(CreateCallback(ChatId, UserId, "ST001"));

        sender.Verify(
            x => x.SendAsync(ChatId, BotMessages.SessionExpiredRestart, It.IsAny<ParseMode?>(), It.IsAny<ReplyMarkup?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleCallback_StopNotFound_SendsStopNotFound()
    {
        var dispatcher = BuildDispatcher(out var sender, out var storage, out var userState, out _);
        storage.Setup(x => x.GetStop("ST999")).Returns((Stop?)null);
        SetupUserWithStop(userState, string.Empty);

        await dispatcher.HandleCallbackAsync(CreateCallback(ChatId, UserId, "ST999"));

        sender.Verify(
            x => x.SendAsync(ChatId, BotMessages.StopNotFound, It.IsAny<ParseMode?>(), It.IsAny<ReplyMarkup?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleCallback_AdminRefresh_TriggersRefresh()
    {
        var dispatcher = BuildDispatcher(out var sender, out var storage, out var userState, out var refresh);
        refresh.Setup(x => x.ForceRefreshAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        storage.Setup(x => x.GetAllStops()).Returns(new List<Stop> { CreateStop() });

        // The admin callback uses AdminId, so the session must be set up for that user.
        userState
            .Setup(x => x.TryGet(AdminId, out It.Ref<UserState?>.IsAny))
            .Returns((long id, out UserState? state) =>
            {
                state = new UserState { SelectedStopId = string.Empty };
                return true;
            });

        await dispatcher.HandleCallbackAsync(CreateCallback(ChatId, AdminId, BotCommands.AdminRefresh));

        refresh.Verify(x => x.ForceRefreshAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleCallback_NoChat_AnswersWithAlert()
    {
        var dispatcher = BuildDispatcher(out var sender, out _, out _, out _);

        var callback = new CallbackQuery
        {
            Id = "cb-2",
            From = new User { Id = UserId, FirstName = "Test" },
            Data = "ST001"
        };

        await dispatcher.HandleCallbackAsync(callback);

        sender.Verify(
            x => x.AnswerCallbackAsync("cb-2", BotMessages.CallbackNoChat, true, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

