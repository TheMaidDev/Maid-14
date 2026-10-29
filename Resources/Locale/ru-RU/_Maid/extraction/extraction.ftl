extraction-notify-popup = Новое сообщение об эвакуации. Проверьте чат.
extraction-unlocked = Задания выполнены. Откройте устройство, нажмите «Обозначить зону для отхода», а находясь в зоне — «Подготовить капсулу». Код капсулы: { $code }.
extraction-unlocked-forced = Эвакуация разрешена досрочно. Откройте устройство, нажмите «Обозначить зону для отхода», а находясь в зоне — «Подготовить капсулу». Код капсулы: { $code }.
extraction-zone-designated = Назначены точки отхода:
    { $location }
    Встаньте в любую из зон и нажмите «Подготовить капсулу» в устройстве. Осталось переносов: { $left }.
extraction-no-requests-zone = Точки отхода больше нельзя перенести. Они по-прежнему здесь:
    { $location }
extraction-no-requests = Запросов на эвакуацию больше не осталось.
extraction-no-zone = Подходящая точка отхода на станции не найдена.
extraction-zone-lost = Все точки отхода уничтожены. Назначены новые:
    { $location }
extraction-zone-lost-none = Все точки отхода уничтожены, а новые найти не удалось. Обратитесь к администрации.
extraction-zone-reset = Можно снова запросить точки отхода и вызвать капсулу.
extraction-reminder = Эвакуация разрешена. Откройте устройство и нажмите «Обозначить зону для отхода». Код капсулы: { $code }.
extraction-reminder-zone = Точки отхода:
    { $location }
    Код капсулы: { $code }.
extraction-not-in-zone = Нужно находиться в зоне отхода.
extraction-zone-coordinates = { $location }, координаты: { $x }, { $y }
extraction-zone-marker-desc = Маркер отхода «{ $name }».
extraction-announcement-sender = Сенсоры станции
extraction-round-end = [color=gold]{ $name }[/color]: эвакуация через { $minutes } { $minutes ->
    [one] минуту
    [few] минуты
   *[other] минут
} после прибытия на станцию.

extraction-pod-announcement = Неизвестный малый объект движется на большой скорости в сторону станции, столкновение неизбежно. Предполагаемое место столкновения: { $location }.
extraction-pod-called = Капсула вызвана и приземлится через { $seconds } { $seconds ->
    [one] секунду
    [few] секунды
   *[other] секунд
} на клетку, где вы стоите. Всё на этой клетке будет уничтожено, отойдите в сторону!
extraction-pod-landed = Капсула приземлилась. Откройте её кодом { $code }, заберитесь внутрь, закройте и нажмите «СТАРТ». Через { $minutes } { $minutes ->
    [one] минуту
    [few] минуты
   *[other] минут
} капсула сама покинет станцию, с вами или без вас.
extraction-pod-leaving-soon = Капсула покинет станцию через { $seconds } { $seconds ->
    [one] секунду
    [few] секунды
   *[other] секунд
}, с вами или без вас!
extraction-pod-left-without = Капсула покинула станцию без вас. Другой не будет.
extraction-pod-lost = Капсула не смогла приземлиться: места посадки больше нет. Вызов потрачен, другого не будет.
extraction-pod-destroyed = Капсула уничтожена. Другой не будет.
extraction-pod-inside = Нажмите «СТАРТ», чтобы взлететь.
extraction-pod-not-inside = Нужно находиться внутри своей капсулы.
extraction-pod-launching = Капсула загерметизирована. Взлёт через { $seconds } { $seconds ->
    [one] секунду
    [few] секунды
   *[other] секунд
}.
extraction-pod-launch-cancelled = Вы покинули капсулу, взлёт отменён.
extraction-pod-sealed = Капсула загерметизирована.
extraction-pod-ui-title = Десантная капсула
extraction-pod-ui-locked = Введите код
extraction-pod-ui-wrong-code = Неверный код
extraction-pod-ui-unlocked = Капсула открыта
extraction-pod-ui-launching = Идёт взлёт

command-description-extraction-reset = Удаляет зону отхода у переданных антагонистов и позволяет запросить её заново.
command-description-extraction-unlock = Открывает отход переданным антагонистам независимо от их целей.

extraction-command-no-entity = Сущность не найдена.
extraction-command-no-component = У { $target } нет компонента AntagExtractable.
extraction-command-already-unlocked = { $target }: отход уже открыт.
extraction-command-unlocked = { $target }: отход открыт.
extraction-command-unlocked-added = { $target }: добавлен AntagExtractable (всё, что сейчас при себе, считается своим снаряжением), отход открыт.
extraction-command-reset = { $target }: зона отхода сброшена.

extraction-device-unrecognized = Мы вас не распознали.
extraction-device-ui-title = Устройство неизвестного назначения
extraction-device-ui-done = Задания для досрочного отхода выполнены
extraction-device-ui-not-done = Задания для досрочного отхода не выполнены
extraction-device-progress-percent = Выполнено { $completed } из { $total } — { $percent }% (нужно { $needed }%)
extraction-device-progress-count = Выполнено { $completed } из { $total } (нужно { $needed })
extraction-device-ui-objective = { $mark } { $title }
extraction-device-ui-objective-required = { $mark } { $title } (обязательно)
extraction-device-ui-zone-status = Зоны отхода: { $zone }
extraction-device-ui-pod-status = Капсула: { $pod }.
extraction-device-ui-rerolls = Количество попыток для переназначения зоны: { $count }
extraction-device-ui-code = Код доступа: { $code }.
extraction-device-ui-code-none = Код доступа: ещё не получен.
extraction-device-ui-refresh = Обновить
extraction-device-ui-zone = Обозначить зону для отхода
extraction-device-ui-pod = Подготовить капсулу
extraction-device-zone-none = не обозначена
extraction-device-pod-none = не вызвана
extraction-device-pod-flying = в пути, посадка через { $seconds } с
extraction-device-pod-landed = на станции, улетит через { $time }
extraction-device-pod-landed-forever = на станции
extraction-device-pod-launching = взлёт через { $seconds } с
extraction-device-pod-gone = больше недоступна
extraction-device-ui-objectives-header = Цели
