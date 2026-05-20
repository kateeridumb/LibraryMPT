# LibraryMPT Mobile Client

Отдельный Flutter-проект для роли клиента (Student).

## Что уже есть

- авторизация по `username/password`;
- хранение и подстановка Bearer-токена;
- экран каталога книг;
- экран деталей книги;
- экран личного кабинета;
- заготовка для отправки заявки на подписочную книгу.

## Важно про авторизацию

Клиентские эндпоинты в `LibraryMPT.Api` защищены JWT (`[Authorize]`), поэтому мобильному приложению нужен валидный access token.

`POST /api/account/login` теперь возвращает `accessToken` (если вход без 2FA), и приложение автоматически сохраняет его в `SharedPreferences`.

Для аккаунтов с включенным 2FA мобильный поток пока не реализован.

## Запуск

1. Установить Flutter SDK.
2. Из папки проекта выполнить:
   - `flutter pub get`
   - `flutter run`

## Настройка URL API

Базовый URL указан в `lib/src/core/config/app_config.dart`.

- Android emulator: `http://10.0.2.2:5000`
- iOS simulator: `http://localhost:5000`
- Физическое устройство: IP вашего ПК в локальной сети
