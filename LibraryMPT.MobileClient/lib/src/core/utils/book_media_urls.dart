import '../config/app_config.dart';

class BookMediaUrls {
  const BookMediaUrls._();

  /// Соответствует [BookCoverWebUrl.NormalizeStoredImagePath] в бэкенде.
  static String? normalizeStoredImagePath(String? imagePath) {
    final raw = imagePath?.trim();
    if (raw == null || raw.isEmpty) return null;
    String s = raw;
    while (true) {
      if (s.length >= 2 &&
          ((s.startsWith('"') && s.endsWith('"')) ||
              (s.startsWith("'") && s.endsWith("'")))) {
        s = s.substring(1, s.length - 1).trim();
        continue;
      }
      if (s.length >= 2 && s.startsWith('\u201c') && s.endsWith('\u201d')) {
        s = s.substring(1, s.length - 1).trim();
        continue;
      }
      break;
    }
    return s.isEmpty ? null : s;
  }

  static bool isRemoteCoverUrl(String? imagePath) {
    final t = normalizeStoredImagePath(imagePath);
    if (t == null || t.isEmpty) {
      return false;
    }
    return t.startsWith('http://') || t.startsWith('https://');
  }

  static String coverByBookId(int bookId) =>
      '${AppConfig.apiBaseUrl}/api/client/mobile/cover/$bookId';

  /// Всегда через API: внешние URL подтягиваются на сервере (не напрямую из приложения).
  static String displayCoverUrl({required int bookId, String? imagePath}) {
    return coverByBookId(bookId);
  }

  static bool displayCoverNeedsAuthorization(String? imagePath) => false;

  static String shareableCoverReference({required int bookId, String? imagePath}) =>
      displayCoverUrl(bookId: bookId, imagePath: imagePath);
}
