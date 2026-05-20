import 'dart:convert';
import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:open_filex/open_filex.dart';
import 'package:path/path.dart' as p;
import 'package:pdfx/pdfx.dart';
import 'package:webview_flutter/webview_flutter.dart';

import '../../../core/theme/app_theme.dart';
import '../data/fb2_plain_text.dart';
import 'reader/paginated_text_reader.dart';

/// Локальный просмотр скачанного в кэш приложения файла книги.
class BookReaderScreen extends StatelessWidget {
  const BookReaderScreen({
    required this.filePath,
    required this.title,
    this.bookId,
    super.key,
  });

  final String filePath;
  final String title;
  final int? bookId;

  static Future<void> openIfSupported(
    BuildContext context,
    String filePath,
    String title, {
    int? bookId,
  }) async {
    if (kIsWeb) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Чтение здесь недоступно — откройте приложение для Android или iOS.')),
      );
      return;
    }

    await Navigator.of(context).push<void>(
      MaterialPageRoute<void>(
        builder: (_) => BookReaderScreen(filePath: filePath, title: title, bookId: bookId),
      ),
    );
  }

  static const Set<String> _textExtensions = <String>{
    '.txt',
    '.text',
    '.md',
    '.markdown',
    '.log',
    '.csv',
  };

  String get ext => p.extension(filePath).toLowerCase();

  @override
  Widget build(BuildContext context) {
    final e = ext;

    if (_textExtensions.contains(e) || e == '.fb2') {
      if (e == '.fb2') {
        return _Fb2Reader(path: filePath, title: title, bookId: bookId);
      }
      return _PlainTextReader(path: filePath, title: title, bookId: bookId);
    }

    return Scaffold(
      appBar: AppBar(
        title: Text(title, maxLines: 1, overflow: TextOverflow.ellipsis),
      ),
      body: _builtInNonTextBody(e),
    );
  }

  Widget _builtInNonTextBody(String e) {
    if (!kIsWeb && e == '.pdf') {
      return _PdfReader(path: filePath);
    }
    if (!kIsWeb && (e == '.htm' || e == '.html' || e == '.xhtml')) {
      return _LocalHtmlReader(path: filePath);
    }
    return _ExternalOpenFallback(path: filePath, ext: e);
  }
}

class _PlainTextReader extends StatefulWidget {
  const _PlainTextReader({
    required this.path,
    required this.title,
    this.bookId,
  });

  final String path;
  final String title;
  final int? bookId;

  @override
  State<_PlainTextReader> createState() => _PlainTextReaderState();
}

class _PlainTextReaderState extends State<_PlainTextReader> {
  String? _text;
  Object? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final raw = await File(widget.path).readAsBytes();
      late final String text;
      try {
        text = utf8.decode(raw, allowMalformed: true);
      } catch (_) {
        text = String.fromCharCodes(raw);
      }
      if (!mounted) {
        return;
      }
      setState(() => _text = text);
    } catch (e) {
      if (mounted) {
        setState(() => _error = e);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final err = _error;
    if (err != null) {
      return Scaffold(
        appBar: AppBar(title: Text(widget.title)),
        body: _ReaderError(message: err.toString()),
      );
    }
    final text = _text;
    if (text == null) {
      return Scaffold(
        appBar: AppBar(title: Text(widget.title)),
        body: const Center(child: CircularProgressIndicator()),
      );
    }
    return PaginatedTextReader(
      fullText: text,
      title: widget.title,
      bookId: widget.bookId,
    );
  }
}

class _Fb2Reader extends StatefulWidget {
  const _Fb2Reader({
    required this.path,
    required this.title,
    this.bookId,
  });

  final String path;
  final String title;
  final int? bookId;

  @override
  State<_Fb2Reader> createState() => _Fb2ReaderState();
}

class _Fb2ReaderState extends State<_Fb2Reader> {
  String? _text;
  Object? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final text = await loadFb2PlainTextFromFile(File(widget.path));
      if (!mounted) {
        return;
      }
      setState(() => _text = text);
    } catch (e) {
      if (mounted) {
        setState(() => _error = e);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final err = _error;
    if (err != null) {
      return Scaffold(
        appBar: AppBar(title: Text(widget.title)),
        body: _ReaderError(message: err.toString()),
      );
    }
    final text = _text;
    if (text == null) {
      return Scaffold(
        appBar: AppBar(title: Text(widget.title)),
        body: const Center(child: CircularProgressIndicator()),
      );
    }
    if (text.isEmpty) {
      return Scaffold(
        appBar: AppBar(title: Text(widget.title)),
        body: const Center(child: Text('В FB2 не удалось извлечь текст.')),
      );
    }
    return PaginatedTextReader(
      fullText: text,
      title: widget.title,
      bookId: widget.bookId,
    );
  }
}

class _PdfReader extends StatefulWidget {
  const _PdfReader({required this.path});

  final String path;

  @override
  State<_PdfReader> createState() => _PdfReaderState();
}

class _PdfReaderState extends State<_PdfReader> {
  PdfControllerPinch? _controller;
  Object? _error;

  @override
  void initState() {
    super.initState();
    _attach();
  }

  Future<void> _attach() async {
    try {
      final ctrl = PdfControllerPinch(document: PdfDocument.openFile(widget.path));
      if (!mounted) {
        ctrl.dispose();
        return;
      }
      setState(() => _controller = ctrl);
    } catch (e, st) {
      debugPrint('PDF open error $e\n$st');
      if (mounted) {
        setState(() => _error = e);
      }
    }
  }

  @override
  void dispose() {
    _controller?.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final err = _error;
    if (err != null) {
      return _ReaderError(message: err.toString());
    }
    final c = _controller;
    if (c == null) {
      return const Center(child: CircularProgressIndicator());
    }
    return PdfViewPinch(controller: c);
  }
}

class _LocalHtmlReader extends StatefulWidget {
  const _LocalHtmlReader({required this.path});

  final String path;

  @override
  State<_LocalHtmlReader> createState() => _LocalHtmlReaderState();
}

class _LocalHtmlReaderState extends State<_LocalHtmlReader> {
  late final WebViewController _controller;

  @override
  void initState() {
    super.initState();
    _controller = WebViewController()
      ..setJavaScriptMode(JavaScriptMode.unrestricted)
      ..setBackgroundColor(AppThemeColors.neutralWhite)
      ..loadRequest(Uri.file(widget.path));
  }

  @override
  Widget build(BuildContext context) {
    return WebViewWidget(controller: _controller);
  }
}

class _ExternalOpenFallback extends StatelessWidget {
  const _ExternalOpenFallback({required this.path, required this.ext});

  final String path;
  final String ext;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: <Widget>[
            Icon(Icons.folder_open_rounded, size: 52, color: AppThemeColors.primaryMedium),
            const SizedBox(height: 16),
            Text(
              'Для «$ext» нет своей читалки в приложении. Откройте файл во внешней программе (например, DJVU или плагиновые форматы).',
              textAlign: TextAlign.center,
              style: Theme.of(context).textTheme.bodyLarge?.copyWith(height: 1.42),
            ),
            const SizedBox(height: 24),
            FilledButton.icon(
              onPressed: () async {
                final r = await OpenFilex.open(path);
                if (!context.mounted) {
                  return;
                }
                ScaffoldMessenger.of(context).showSnackBar(
                  SnackBar(content: Text(r.message)),
                );
              },
              icon: const Icon(Icons.open_in_new),
              label: const Text('Открыть во внешнем приложении'),
            ),
          ],
        ),
      ),
    );
  }
}

class _ReaderError extends StatelessWidget {
  const _ReaderError({required this.message});

  final String message;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(20),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: <Widget>[
            const Icon(Icons.error_outline, color: Colors.redAccent, size: 40),
            const SizedBox(height: 12),
            Text(message, textAlign: TextAlign.center),
          ],
        ),
      ),
    );
  }
}
