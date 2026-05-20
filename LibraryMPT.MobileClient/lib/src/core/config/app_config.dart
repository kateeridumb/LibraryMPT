import 'package:flutter/foundation.dart';

class AppConfig {
  const AppConfig._();

  /// Базовый URL API. Для телефона в той же Wi‑Fi сети — IP ПК, где запущен LibraryMPT.Api (порт как в launchSettings, обычно 5007).
  /// Обложки: [BookMediaUrls.coverByBookId] → `GET {apiBaseUrl}/api/client/mobile/cover/{id}` (локальные файлы из wwwroot).
  static String get apiBaseUrl {
    if (kIsWeb) {
      return 'https://localhost:7192';
    }
    return 'http://192.168.0.106:5007';
  }
}
