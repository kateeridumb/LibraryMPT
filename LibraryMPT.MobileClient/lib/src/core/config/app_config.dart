import 'package:flutter/foundation.dart';

class AppConfig {
  const AppConfig._();

  /// Базовый URL развёрнутого сервера. Пути в репозиториях уже включают префикс `/api/...`,
  /// поэтому здесь — только схема + хост (без `/api` на конце).
  /// Обложки: [BookMediaUrls.coverByBookId] → `GET {apiBaseUrl}/api/client/mobile/cover/{id}`.
  static String get apiBaseUrl {
    return 'http://212.113.121.116';
  }
}
