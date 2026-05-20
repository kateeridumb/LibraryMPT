import 'dart:io';

import 'package:file_saver/file_saver.dart';
import 'package:flutter/foundation.dart';
import 'package:gal/gal.dart';
import 'package:http/http.dart' as http;
import 'package:path_provider/path_provider.dart';
import 'package:share_plus/share_plus.dart';

import '../../../core/network/api_client.dart';
import '../../../core/network/api_exception.dart';
import '../../common/models.dart';
import 'reader_api_models.dart';

class BooksRepository {
  BooksRepository(this._apiClient);

  final ApiClient _apiClient;

  Future<ClientIndexData> loadBooks({
    String? search,
    List<int>? categoryIds,
  }) async {
    final queryParameters = search == null || search.trim().isEmpty
        ? null
        : <String, String>{'search': search.trim()};
    Map<String, List<String>>? queryParametersAll;
    if (categoryIds != null && categoryIds.isNotEmpty) {
      queryParametersAll = <String, List<String>>{
        'categoryIds': categoryIds.map((e) => '$e').toList(),
      };
    }
    final response = await _apiClient.getJson(
      '/api/client/index',
      authorized: true,
      queryParameters: queryParameters,
      queryParametersAll: queryParametersAll,
    );
    return ClientIndexData.fromJson(response);
  }

  Future<BookDetailsData> loadBookDetails(int bookId) async {
    final response = await _apiClient.getJson(
      '/api/client/book-details/$bookId',
      authorized: true,
    );
    return BookDetailsData.fromJson(response);
  }

  Future<String> createBookRequest(int bookId) async {
    final response = await _apiClient.postJson(
      '/api/client/book-requests',
      authorized: true,
      body: <String, dynamic>{'bookId': bookId},
    );
    return (response['message'] ?? 'Заявка отправлена.').toString();
  }

  Future<String?> markBookRead(int bookId) async {
    final response = await _apiClient.postJson(
      '/api/client/mark-read',
      authorized: true,
      body: <String, dynamic>{'bookId': bookId, 'userId': 0},
    );
    final success = response['success'] as bool? ?? false;
    if (!success) {
      throw ApiException((response['message'] ?? 'Не удалось отметить книгу').toString());
    }
    return response['message'] as String?;
  }

  /// Сохраняет файл книги через системный механизм (Downloads / диалог сохранения).
  Future<String> saveBookFileOnDevice(int bookId) async {
    final binary = await _apiClient.getBytes(
      '/api/client/download/$bookId',
      authorized: true,
    );
    final bytes = Uint8List.fromList(binary.bodyBytes);
    final rawName = _sanitizeFileName(binary.suggestedFileName ?? 'book_$bookId');
    final split = _splitNameAndExtension(rawName);
    final mime = _mimeForBookExtension(split.extension);

    final pathOrMessage = await FileSaver.instance.saveFile(
      name: split.baseName,
      bytes: bytes,
      fileExtension: split.extension.isEmpty ? 'bin' : split.extension,
      mimeType: mime.mimeType,
      customMimeType: mime.customMimeType,
    );
    if (pathOrMessage.isEmpty) {
      return 'Файл книги сохранён.';
    }
    return pathOrMessage;
  }

  /// Открывает меню «Поделиться», чтобы сохранить книгу вручную.
  Future<String> fetchAndShareBookDownload(int bookId) async {
    final binary = await _apiClient.getBytes(
      '/api/client/download/$bookId',
      authorized: true,
    );
    final dir = await getTemporaryDirectory();
    final safeName = _sanitizeFileName(binary.suggestedFileName ?? 'book_$bookId');
    final file = File('${dir.path}/$safeName');
    await file.writeAsBytes(binary.bodyBytes, flush: true);
    await Share.shareXFiles(
      <XFile>[XFile(file.path)],
      subject: safeName,
    );
    return safeName;
  }

  /// Сохраняет файл в каталог приложения для встроенной читалки (повторная загрузка перезаписывает файл).
  Future<String> cacheBookFileForReading(int bookId) async {
    final binary = await _apiClient.getBytes(
      '/api/client/download/$bookId',
      authorized: true,
    );
    final dir = await getApplicationDocumentsDirectory();
    final folder = Directory('${dir.path}/library_reader_cache');
    if (!await folder.exists()) {
      await folder.create(recursive: true);
    }
    final rawSuggested = binary.suggestedFileName ?? 'book_$bookId.bin';
    final safe = _sanitizeFileName(rawSuggested);
    final file = File('${folder.path}/${bookId}_$safe');
    await file.writeAsBytes(binary.bodyBytes, flush: true);
    return file.path;
  }

  Future<ReadingProgressApi?> getReadingProgress(int bookId) async {
    final response = await _apiClient.getJsonOrNull(
      '/api/client/reading-progress',
      authorized: true,
      queryParameters: <String, String>{'bookId': '$bookId'},
    );
    if (response == null) {
      return null;
    }
    return ReadingProgressApi.fromJson(response);
  }

  Future<void> saveReadingProgress({
    required int bookId,
    int? page,
    String? position,
    int? percent,
  }) async {
    await _apiClient.postJson(
      '/api/client/save-progress',
      authorized: true,
      body: <String, dynamic>{
        'bookId': bookId,
        if (page != null) 'page': page,
        if (position != null) 'position': position,
        if (percent != null) 'percent': percent,
      },
    );
  }

  Future<List<BookmarkApi>> listBookmarks(int bookId) async {
    final raw = await _apiClient.getJsonList(
      '/api/client/bookmarks',
      authorized: true,
      queryParameters: <String, String>{'bookId': '$bookId'},
    );
    return raw
        .whereType<Map<String, dynamic>>()
        .map(BookmarkApi.fromJson)
        .toList();
  }

  Future<void> addBookmark({
    required int bookId,
    required String pageLabel,
    String? position,
    String? title,
    String? note,
  }) async {
    await _apiClient.postJson(
      '/api/client/bookmarks',
      authorized: true,
      body: <String, dynamic>{
        'userId': 0,
        'bookmark': <String, dynamic>{
          'bookID': bookId,
          'page': pageLabel,
          if (position != null) 'position': position,
          if (title != null && title.isNotEmpty) 'title': title,
          if (note != null && note.isNotEmpty) 'note': note,
        },
      },
    );
  }

  Future<void> deleteBookmark(int bookmarkId) async {
    final response = await _apiClient.deleteJson(
      '/api/client/bookmarks/$bookmarkId',
      authorized: true,
    );
    final ok = response['success'] as bool? ?? true;
    if (!ok) {
      throw ApiException((response['message'] ?? 'Не удалось удалить закладку').toString());
    }
  }

  /// Обложку в галерею (Android / iOS) или диалог сохранения на desktop.
  Future<List<int>> _fetchCoverRawBytes(int bookId, {String? imagePath}) async {
    final t = imagePath?.trim();
    if (t != null &&
        t.isNotEmpty &&
        (t.startsWith('http://') || t.startsWith('https://'))) {
      final response = await http.get(Uri.parse(t));
      if (response.statusCode >= 200 && response.statusCode < 300) {
        return response.bodyBytes;
      }
      throw ApiException(
        'Обложка недоступна (HTTP ${response.statusCode})',
        statusCode: response.statusCode,
      );
    }
    final binary = await _apiClient.getBytes(
      '/api/client/mobile/cover/$bookId',
      authorized: true,
    );
    return binary.bodyBytes;
  }

  Future<String> saveCoverToGallery(int bookId, {String? imagePath}) async {
    final raw = await _fetchCoverRawBytes(bookId, imagePath: imagePath);
    final bytes = Uint8List.fromList(raw);

    if (!kIsWeb && _isMobileOs) {
      try {
        if (!await Gal.hasAccess(toAlbum: true)) {
          final granted = await Gal.requestAccess(toAlbum: true);
          if (!granted) {
            throw ApiException('Нет доступа к галерее. Разрешите сохранение в настройках.');
          }
        }
        await Gal.putImageBytes(bytes, name: 'LibraryMPT_cover_$bookId');
        return 'Обложка сохранена в галерею.';
      } on GalException catch (e) {
        throw ApiException('Галерея: ${e.platformException.message ?? e.toString()}');
      }
    }

    final ext = _guessExtensionFromBytes(raw).replaceFirst('.', '');
    final cleanExt = ext.isEmpty ? 'png' : ext;
    final mime = _mimeForCoverExtension(cleanExt);
    final message = await FileSaver.instance.saveFile(
      name: 'LibraryMPT_cover_$bookId',
      bytes: bytes,
      fileExtension: cleanExt,
      mimeType: mime.mimeType,
      customMimeType: mime.customMimeType,
    );
    return message.isEmpty ? 'Файл обложки сохранён.' : message;
  }

  Future<String> fetchCoverToTemp(int bookId, {String? imagePath}) async {
    final raw = await _fetchCoverRawBytes(bookId, imagePath: imagePath);
    final dir = await getTemporaryDirectory();
    final ext = _guessExtensionFromBytes(raw);
    final safe = 'cover_$bookId$ext';
    final file = File('${dir.path}/$safe');
    await file.writeAsBytes(raw, flush: true);
    return file.path;
  }

  Future<void> shareCoverImage(int bookId, String bookTitle, {String? imagePath}) async {
    final path = await fetchCoverToTemp(bookId, imagePath: imagePath);
    await Share.shareXFiles(
      <XFile>[XFile(path, mimeType: 'image/*')],
      text: bookTitle,
    );
  }

  bool get _isMobileOs {
    if (kIsWeb) {
      return false;
    }
    try {
      return Platform.isAndroid || Platform.isIOS;
    } catch (_) {
      return false;
    }
  }

  static String _sanitizeFileName(String raw) {
    var s = raw.replaceAll(RegExp(r'[<>:"/\\|?*]'), '_').trim();
    if (s.isEmpty) {
      s = 'download';
    }
    return s;
  }

  static ({String baseName, String extension}) _splitNameAndExtension(String rawName) {
    final lastDot = rawName.lastIndexOf('.');
    if (lastDot <= 0 || lastDot == rawName.length - 1) {
      return (baseName: rawName, extension: '');
    }
    return (
      baseName: rawName.substring(0, lastDot),
      extension: rawName.substring(lastDot + 1),
    );
  }

  static _MimeChoice _mimeForBookExtension(String ext) {
    switch (ext.toLowerCase()) {
      case 'pdf':
        return _MimeChoice(MimeType.pdf);
      case 'epub':
        return _MimeChoice(MimeType.epub);
      case 'zip':
        return _MimeChoice(MimeType.zip);
      case 'txt':
        return _MimeChoice(MimeType.text);
      case 'html':
      case 'htm':
        return _MimeChoice(MimeType.custom, customMimeType: 'text/html');
      default:
        return _MimeChoice(MimeType.other);
    }
  }

  static _MimeChoice _mimeForCoverExtension(String ext) {
    switch (ext.toLowerCase()) {
      case 'jpg':
      case 'jpeg':
        return _MimeChoice(MimeType.jpeg);
      case 'png':
        return _MimeChoice(MimeType.png);
      case 'webp':
        return _MimeChoice(MimeType.webp);
      case 'gif':
        return _MimeChoice(MimeType.gif);
      default:
        return _MimeChoice(MimeType.png);
    }
  }

  static String _guessExtensionFromBytes(List<int> bytes) {
    if (bytes.length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff) {
      return '.jpg';
    }
    if (bytes.length >= 8 &&
        bytes[0] == 0x89 &&
        bytes[1] == 0x50 &&
        bytes[2] == 0x4e &&
        bytes[3] == 0x47) {
      return '.png';
    }
    if (bytes.length >= 12 &&
        bytes[0] == 0x52 &&
        bytes[1] == 0x49 &&
        bytes[2] == 0x46 &&
        bytes[3] == 0x46) {
      return '.webp';
    }
    return '.img';
  }
}

class _MimeChoice {
  _MimeChoice(this.mimeType, {this.customMimeType});

  final MimeType mimeType;
  final String? customMimeType;
}
