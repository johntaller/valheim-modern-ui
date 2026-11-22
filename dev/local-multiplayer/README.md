# Локальный мультиплеер Valheim New UI

Тестовый модуль запускает два процесса Valheim через штатный TCP-транспорт игры на `127.0.0.1:24679`. Хост открывает порт только для мира и персонажа DEVBLACKT, запущенных в одиночном режиме. Отдельный клиент подключается локальным персонажем DEVBLACKT2. Для пары процессов достаточно одной установленной игры.

Соберите релизный мод и отдельный тестовый модуль из корня этой ветки.

```sh
game_dir="$HOME/Library/Application Support/Steam/steamapps/common/Valheim"
dotnet build -c Release -p:GameDir="$game_dir"
dotnet build dev/local-multiplayer -c Release -p:GameDir="$game_dir"
```

Для независимых каталогов BepInEx сделайте копию `valheim.app`, `BepInEx`, `doorstop_libs`, `doorstop_config.ini`, `steam_appid.txt` и `start_game_bepinex.sh` в `.scratch/dev-client`. Положите обе DLL в `BepInEx/plugins/ValheimNewUI` каждого клиента.

1. В каталоге основной игры запустите `./start_game_bepinex.sh -newui-local-host`. В меню выберите DEVBLACKT и войдите в мир DEVBLACKT с выключенной галочкой «Запустить сервер».
2. В каталоге копии запустите `./start_game_bepinex.sh -newui-local-client -screen-fullscreen 0 -screen-width 1100 -screen-height 700`. Нажмите «Начать игру». Если DEVBLACKT2 ещё отсутствует, откройте панель «Новый», и тестовый модуль запишет локальный профиль. Готовый профиль выбирается и подключается автоматически.
3. После выхода обоих клиентов уберите `ValheimNewUI.LocalTest.dll` из папки установленной игры. Обычному Valheim New UI требуется только его собственная DLL.

Сетевое подключение работает через два настоящих экземпляра игры, каждый со своим персонажем. Модуль предназначен для изолированной проверки карт игроков. В корневую сборку `ValheimNewUI.dll` входит только основной мод.
