using MaybeComingBot.Abstractions;
using MaybeComingBot.Constants;
using MaybeComingBot.Utils;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace MaybeComingBot.Services;

/// <summary>
/// Handles incoming Telegram updates and routes them to the appropriate handlers.
/// </summary>
/// <remarks>
/// This is the main entry point for all user interactions. It processes messages,
/// callback queries, and other update types, applying rate limiting and user state
/// management before dispatching to <see cref="CommandDispatcher"/>.
/// </remarks>
public class BotHandler(
    ITelegramBotClient botClient,
    IUserStateService userStateService,
    CommandDispatcher dispatcher,
    RateLimiter rateLimiter,
    ILogger<BotHandler> logger)
{
    private readonly ITelegramBotClient _botClient = botClient;
    private readonly IUserStateService _userStateService = userStateService;
    private readonly CommandDispatcher _dispatcher = dispatcher;
    private readonly RateLimiter _rateLimiter = rateLimiter;
    private readonly ILogger<BotHandler> _logger = logger;

    /// <summary>
    /// Processes an incoming update from Telegram.
    /// </summary>
    /// <param name="update">The update received from Telegram.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// The handling flow:
    /// <list type="number">
    /// <item><description>Extracts user and chat IDs from the update</description></item>
    /// <item><description>Updates user activity timestamp</description></item>
    /// <item><description>Routes callback queries to <see cref="CommandDispatcher.HandleCallbackAsync"/></description></item>
    /// <item><description>Skips non-message updates, edited messages, and bot messages</description></item>
    /// <item><description>Stores user information (name, username)</description></item>
    /// <item><description>Applies rate limiting</description></item>
    /// <item><description>Parses command text and dispatches to <see cref="CommandDispatcher.DispatchCommandAsync"/></description></item>
    /// </list>
    /// All exceptions are caught and logged to prevent the polling loop from crashing.
    /// </remarks>
    public async Task HandleUpdateAsync(Update update)
    {
        // Calculate once for performance
        var userId = GetUserId(update);
        var chatId = GetChatId(update);

        try
        {
            // Ignore edited messages
            if (update.Type == UpdateType.EditedMessage)
                return;

            // Update user activity (if not a bot)
            if (userId > 0)
                _userStateService.UpdateActivity(userId);

            // Handle CallbackQuery
            if (update.Type == UpdateType.CallbackQuery)
            {
                if (update.CallbackQuery is not null)
                    await _dispatcher.HandleCallbackAsync(update.CallbackQuery);
                return;
            }

            // Process only messages
            if (update.Type != UpdateType.Message)
                return;

            var message = update.Message;
            if (message is null || string.IsNullOrWhiteSpace(message.Text))
                return;

            // Ignore messages from bots
            if (message.From?.IsBot == true)
                return;

            // Store user information
            if (message.From is not null)
            {
                _userStateService.SetUserInfo(
                    userId,
                    message.From.FirstName ?? string.Empty,
                    message.From.LastName,
                    message.From.Username);
            }

            // Apply rate limiting
            if (!await _rateLimiter.IsAllowedAsync(userId))
            {
                await _botClient.SendMessage(chatId, BotMessages.TooManyRequests);
                return;
            }

            // Parse and dispatch command
            var (command, args) = ParseCommand(message.Text.Trim());
            await _dispatcher.DispatchCommandAsync(chatId, userId, command, args);
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Update handling cancelled (OperationCanceled)");
        }
        catch (TaskCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Update handling cancelled (TaskCanceled)");
        }
        catch (ApiRequestException apiEx)
        {
            _logger.LogWarning(apiEx, "Telegram API error: {ErrorCode}", apiEx.ErrorCode);
            // Don't rethrow — bot continues working
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error handling update (UserId: {UserId}, ChatId: {ChatId})",
                userId, chatId);
            // Don't rethrow — bot continues working
        }
    }

    /// <summary>
    /// Extracts the user ID from an update.
    /// </summary>
    /// <param name="update">The update to extract from.</param>
    /// <returns>The user ID, or 0 if not found.</returns>
    private static long GetUserId(Update update)
    {
        if (update?.Message?.From != null)
            return update.Message.From.Id;

        if (update?.CallbackQuery?.From != null)
            return update.CallbackQuery.From.Id;

        return 0;
    }

    /// <summary>
    /// Extracts the chat ID from an update.
    /// </summary>
    /// <param name="update">The update to extract from.</param>
    /// <returns>The chat ID, or 0 if not found.</returns>
    private static long GetChatId(Update update)
    {
        if (update?.Message?.Chat != null)
            return update.Message.Chat.Id;

        if (update?.CallbackQuery?.Message?.Chat != null)
            return update.CallbackQuery.Message.Chat.Id;

        // For inline mode (no chat)
        return 0;
    }

    /// <summary>
    /// Parses a text message into a command and its arguments.
    /// </summary>
    /// <param name="text">The raw text from the user.</param>
    /// <returns>
    /// A tuple containing the command and optional arguments.
    /// </returns>
    /// <remarks>
    /// <para>
    /// For slash commands:
    /// <list type="bullet">
    /// <item><description>"@botname" suffix is stripped from the command</description></item>
    /// <item><description>Arguments are extracted after the first space</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// For non-slash text: returns the entire text as the command with no arguments.
    /// </para>
    /// </remarks>
    private static (string Command, string? Args) ParseCommand(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (string.Empty, null);

        if (text.StartsWith('/'))
        {
            var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var command = parts[0];

            // Strip @BotName suffix (e.g., /command@MyBot -> /command)
            var atIndex = command.IndexOf('@');
            if (atIndex > 0)
                command = command[..atIndex];

            return (command, parts.Length > 1 ? parts[1].Trim() : null);
        }

        return (text, null);
    }
}