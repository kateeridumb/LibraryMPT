import 'dart:convert';

/// Ответ API `reading-progress`.
class ReadingProgressApi {
  ReadingProgressApi({
    required this.bookId,
    this.lastPage,
    this.lastPosition,
    this.percent,
  });

  final int bookId;
  final int? lastPage;
  final String? lastPosition;
  final int? percent;

  factory ReadingProgressApi.fromJson(Map<String, dynamic> j) {
    return ReadingProgressApi(
      bookId: (j['bookID'] ?? j['bookId'] ?? 0) as int,
      lastPage: j['lastPage'] as int?,
      lastPosition: j['lastPosition'] as String?,
      percent: j['percent'] as int?,
    );
  }
}

/// Элемент `GET /api/client/bookmarks`.
class BookmarkApi {
  BookmarkApi({
    required this.bookmarkId,
    required this.bookId,
    this.page,
    this.position,
    this.title,
    this.note,
    required this.createdAt,
  });

  final int bookmarkId;
  final int bookId;
  final String? page;
  final String? position;
  final String? title;
  final String? note;
  final String? createdAt;

  factory BookmarkApi.fromJson(Map<String, dynamic> j) {
    return BookmarkApi(
      bookmarkId: (j['bookmarkID'] ?? j['bookmarkId'] ?? 0) as int,
      bookId: (j['bookID'] ?? j['bookId'] ?? 0) as int,
      page: j['page'] as String?,
      position: j['position'] as String?,
      title: j['title'] as String?,
      note: j['note'] as String?,
      createdAt: j['createdAt']?.toString(),
    );
  }
}

/// Восстановить индекс страницы (0-based) из сохранённого прогресса.
int? resolveInitialPageIndex({
  required ReadingProgressApi? progress,
  required int totalPages,
}) {
  if (totalPages <= 0) {
    return null;
  }
  final maxIdx = totalPages - 1;
  if (progress == null) {
    return null;
  }
  final pos = progress.lastPosition?.trim();
  if (pos != null && pos.isNotEmpty) {
    try {
      final m = jsonDecode(pos);
      if (m is Map<String, dynamic>) {
        final p = m['page'] ?? m['mobilePage'];
        if (p is int) {
          return p.clamp(0, maxIdx);
        }
        if (p is num) {
          return p.toInt().clamp(0, maxIdx);
        }
      }
    } catch (_) {}
  }
  final lp = progress.lastPage;
  if (lp != null && lp > 0) {
    return (lp - 1).clamp(0, maxIdx);
  }
  final pc = progress.percent;
  if (pc != null && pc > 0 && pc <= 100) {
    return ((pc / 100) * maxIdx).round().clamp(0, maxIdx);
  }
  return null;
}
