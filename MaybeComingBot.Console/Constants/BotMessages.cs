namespace MaybeComingBot.Constants;

public static class BotMessages
{
    public const string Welcome = "🚌 Привет! Я бот для просмотра расписания автобусов.\n\nВыбери остановку:";
    public const string StopNotFound = "⚠️ Остановка не найдена. Попробуйте /start заново.";
    public const string NoSchedule = "⚠️ Расписание временно недоступно. Попробуйте позже или выполните /update.";
    public const string ChooseStopFirst = "Пожалуйста, сначала выберите остановку через /start.";
    public const string UpdateSuccess = "✅ Расписание успешно обновлено!";
    public const string UpdateFailed = "Ошибка обновления расписания. Попробуйте позже.";
    public const string UpdateLoading = "⏳ Загружаю свежее расписание...";
    public const string NoRights = "⛔ У вас нет прав для обновления расписания.";
    public const string UnknownCommand = "Неизвестная команда. Используйте /help для справки.";

    public static string StopSelected(string stopName) => $"✅ Выбрана остановка: *{stopName}*\n\nТеперь используй кнопки ниже:";
    public static string NoTripsToday(string stopName, DayOfWeek day) => $"Сегодня ({day}) рейсов на остановке \"{stopName}\" нет.";
    public static string NoTripsTomorrow(DayOfWeek day) => $"Сегодня рейсов больше нет. Завтра ({day}) рейсов тоже нет.";
    public static string NearestTomorrow(DayOfWeek day, string time) => $"Сегодня рейсов больше нет.\nБлижайший завтра ({day}) в {time}.";

    public static string ReportInstructions(string adminLink) =>
        $"📨 *Связаться с администратором*\n\n" +
        $"Если вы нашли ошибку в расписании или у вас есть предложения, " +
        $"напишите администратору:\n\n" +
        $"[👤 Написать админу]({adminLink})\n\n" +
        $"Или отправьте сообщение командой:\n" +
        $"`/report текст сообщения`\n\n" +
        $"*Важно:* при отправке сообщения укажите:\n" +
        $"• Остановку\n" +
        $"• Время\n" +
        $"• Что не так";

    public static string Help()
    {
        return "📖 *Доступные команды:*\n\n" +
               "/start — начать работу и выбрать остановку\n" +
               "/update — обновить расписание (для всех пользователей)\n" +
               "/report — сообщить об ошибке администратору\n" +
               "/status — статус бота (только для админа)\n" +
               "/cancel — отменить текущее действие\n" +
               "/help — показать эту справку\n\n" +
               "После выбора остановки доступны кнопки:\n" +
               "• *Ближайший* — показать ближайшие рейсы\n" +
               "• *Сегодня* — все рейсы на сегодня\n" +
               "• *Неделя* — расписание на неделю\n" +
               "• *Сменить остановку* — вернуться к выбору\n" +
               "• *📨 Сообщить об ошибке* — написать администратору\n" +
               "• *🔙 Назад* — вернуться в главное меню";
    }
}