using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace MaybeComingBot.Abstractions;

/// <summary>
/// Defines the contract for sending messages and interacting with Telegram API.
/// </summary>
public interface ITelegramSender
{
    /// <summary>
    /// Sends a message to a Telegram chat.
    /// </summary>
    Task SendAsync(
        long chatId,
        string text,
        ParseMode? parseMode = null,
        ReplyMarkup? replyMarkup = null,
        CancellationToken ct = default);

    /// <summary>
    /// Answers a callback query to remove the loading indicator.
    /// </summary>
    Task AnswerCallbackAsync(
        string callbackQueryId,
        string? text = null,
        bool showAlert = false,
        CancellationToken ct = default);

    /// <summary>
    /// Edits an existing message.
    /// </summary>
    Task EditMessageAsync(
        long chatId,
        int messageId,
        string text,
        ParseMode? parseMode = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default);

    /// <summary>
    /// Sends a chat action (typing, uploading, etc.) to indicate bot activity.
    /// </summary>
    Task SendChatActionAsync(
        long chatId,
        ChatAction chatAction,
        CancellationToken ct = default);
}