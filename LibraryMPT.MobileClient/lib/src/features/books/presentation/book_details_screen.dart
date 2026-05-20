import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:provider/provider.dart';

import '../../../core/network/api_client.dart';
import '../../../core/network/api_exception.dart';
import '../../../core/theme/app_theme.dart';
import '../../../core/utils/book_media_urls.dart';
import 'book_reader_screen.dart';
import '../../common/models.dart';
import '../data/books_repository.dart';
import 'widgets/book_cover_image.dart';

class BookDetailsScreen extends StatefulWidget {
  const BookDetailsScreen({required this.bookId, super.key});

  final int bookId;

  @override
  State<BookDetailsScreen> createState() => _BookDetailsScreenState();
}

class _BookDetailsScreenState extends State<BookDetailsScreen> {
  bool _loading = true;
  bool _sendingRequest = false;
  bool _busyAction = false;
  String? _error;
  BookDetailsData? _details;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final data = await context.read<BooksRepository>().loadBookDetails(widget.bookId);
      if (!mounted) {
        return;
      }
      setState(() {
        _details = data;
      });
    } on ApiException catch (error) {
      setState(() {
        _error = error.message;
      });
    } catch (_) {
      setState(() {
        _error = 'Не удалось загрузить детали книги.';
      });
    } finally {
      if (mounted) {
        setState(() {
          _loading = false;
        });
      }
    }
  }

  Future<void> _sendBookRequest() async {
    setState(() {
      _sendingRequest = true;
    });
    try {
      final message = await context.read<BooksRepository>().createBookRequest(widget.bookId);
      if (!mounted) {
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(message)));
      await _load();
    } on ApiException catch (error) {
      if (!mounted) {
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(error.message)));
    } finally {
      if (mounted) {
        setState(() {
          _sendingRequest = false;
        });
      }
    }
  }

  Future<void> _saveCoverToGalleryResolved() async {
    setState(() {
      _busyAction = true;
    });
    try {
      final msg = await context.read<BooksRepository>().saveCoverToGallery(
            widget.bookId,
            imagePath: _details?.book?.imagePath,
          );
      if (!mounted) {
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(msg)));
    } on ApiException catch (error) {
      if (!mounted) {
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(error.message)));
    } finally {
      if (mounted) {
        setState(() {
          _busyAction = false;
        });
      }
    }
  }

  Future<void> _shareCoverResolved() async {
    final title = _details?.book?.title.trim().isNotEmpty == true ? _details!.book!.title : 'Книга';
    setState(() {
      _busyAction = true;
    });
    try {
      await context.read<BooksRepository>().shareCoverImage(
            widget.bookId,
            title,
            imagePath: _details?.book?.imagePath,
          );
    } on ApiException catch (error) {
      if (!mounted) {
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(error.message)));
    } finally {
      if (mounted) {
        setState(() {
          _busyAction = false;
        });
      }
    }
  }

  Future<void> _copyCoverUrl() async {
    final url = BookMediaUrls.shareableCoverReference(
      bookId: widget.bookId,
      imagePath: _details?.book?.imagePath,
    );
    await Clipboard.setData(ClipboardData(text: url));
    if (!mounted) {
      return;
    }
    ScaffoldMessenger.of(context).showSnackBar(
      const SnackBar(content: Text('Ссылка на обложку скопирована')),
    );
  }

  Future<void> _markReadResolved() async {
    setState(() {
      _busyAction = true;
    });
    try {
      await context.read<BooksRepository>().markBookRead(widget.bookId);
      if (!mounted) {
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Отмечено как прочитанное.')),
      );
      await _load();
    } on ApiException catch (error) {
      if (!mounted) {
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(error.message)));
    } finally {
      if (mounted) {
        setState(() {
          _busyAction = false;
        });
      }
    }
  }

  Future<void> _saveBookFileResolved() async {
    setState(() {
      _busyAction = true;
    });
    try {
      final hint = await context.read<BooksRepository>().saveBookFileOnDevice(widget.bookId);
      if (!mounted) {
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(hint)));
    } on ApiException catch (error) {
      if (!mounted) {
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(error.message)));
    } finally {
      if (mounted) {
        setState(() {
          _busyAction = false;
        });
      }
    }
  }

  Future<void> _readBookResolved() async {
    setState(() {
      _busyAction = true;
    });
    try {
      final path = await context.read<BooksRepository>().cacheBookFileForReading(widget.bookId);
      final title = _details?.book?.title.trim().isNotEmpty == true ? _details!.book!.title : 'Книга';
      if (!mounted) {
        return;
      }
      await BookReaderScreen.openIfSupported(
            context,
            path,
            title,
            bookId: widget.bookId,
          );
    } on ApiException catch (error) {
      if (!mounted) {
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(error.message)));
    } catch (_) {
      if (!mounted) {
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Не удалось подготовить файл для чтения.')),
      );
    } finally {
      if (mounted) {
        setState(() {
          _busyAction = false;
        });
      }
    }
  }

  Future<void> _shareBookFileResolved() async {
    setState(() {
      _busyAction = true;
    });
    try {
      final name = await context.read<BooksRepository>().fetchAndShareBookDownload(widget.bookId);
      if (!mounted) {
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Поделиться / сохранить через другое приложение: $name')),
      );
    } on ApiException catch (error) {
      if (!mounted) {
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(error.message)));
    } finally {
      if (mounted) {
        setState(() {
          _busyAction = false;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final details = _details;
    final book = details?.book;
    final apiClient = context.read<ApiClient>();
    return Scaffold(
      appBar: AppBar(title: const Text('Детали книги')),
      body: SafeArea(
        child: _loading
            ? const Center(child: CircularProgressIndicator())
            : _error != null
                ? Center(child: Text(_error!))
                : book == null
                    ? const Center(child: Text('Книга не найдена.'))
                    : ListView(
                        padding: const EdgeInsets.fromLTRB(14, 8, 14, 20),
                        children: <Widget>[
                          Container(
                            padding: const EdgeInsets.all(16),
                            decoration: BoxDecoration(
                              gradient: AppThemeColors.primaryGradient,
                              borderRadius: BorderRadius.circular(20),
                            ),
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: <Widget>[
                                Text(
                                  book.title,
                                  maxLines: 4,
                                  overflow: TextOverflow.ellipsis,
                                  style: Theme.of(context).textTheme.headlineSmall?.copyWith(
                                        color: Colors.white,
                                        fontWeight: FontWeight.w800,
                                      ),
                                ),
                                const SizedBox(height: 8),
                                if (book.author?.fullName.isNotEmpty == true)
                                  Text(
                                    'Автор: ${book.author!.fullName}',
                                    maxLines: 2,
                                    overflow: TextOverflow.ellipsis,
                                    style: const TextStyle(color: Colors.white70),
                                  ),
                                if (book.publishYear != null)
                                  Text(
                                    'Год издания: ${book.publishYear}',
                                    maxLines: 1,
                                    overflow: TextOverflow.ellipsis,
                                    style: const TextStyle(color: Colors.white70),
                                  ),
                                if (book.publisher?.name.isNotEmpty == true)
                                  Text(
                                    'Издатель: ${book.publisher!.name}',
                                    maxLines: 2,
                                    overflow: TextOverflow.ellipsis,
                                    style: const TextStyle(color: Colors.white70),
                                  ),
                              ],
                            ),
                          ),
                          const SizedBox(height: 12),
                          Center(
                            child: Container(
                              constraints: const BoxConstraints(maxWidth: 240),
                              padding: const EdgeInsets.all(12),
                              decoration: BoxDecoration(
                                color: Colors.white,
                                borderRadius: BorderRadius.circular(22),
                                boxShadow: <BoxShadow>[
                                  BoxShadow(
                                    color: AppThemeColors.primaryDark.withValues(alpha: 0.1),
                                    blurRadius: 16,
                                    offset: const Offset(0, 8),
                                  ),
                                ],
                              ),
                              child: AspectRatio(
                                aspectRatio: 2 / 3,
                                child: BookCoverImage(
                                  bookId: book.bookId,
                                  imagePath: book.imagePath,
                                  apiClient: apiClient,
                                  fillParent: true,
                                  fit: BoxFit.contain,
                                  borderRadius: 14,
                                  padding: const EdgeInsets.all(4),
                                ),
                              ),
                            ),
                          ),
                          const SizedBox(height: 10),
                          Wrap(
                            spacing: 8,
                            runSpacing: 8,
                            alignment: WrapAlignment.center,
                            children: <Widget>[
                              FilledButton.icon(
                                onPressed:
                                    (_busyAction || _sendingRequest || _loading) ? null : _saveCoverToGalleryResolved,
                                icon: const Icon(Icons.photo_library_outlined),
                                label: const Text('В галерею'),
                              ),
                              FilledButton.icon(
                                style: FilledButton.styleFrom(
                                  backgroundColor: AppThemeColors.primaryMedium,
                                  foregroundColor: Colors.white,
                                ),
                                onPressed:
                                    (_busyAction || _sendingRequest || _loading) ? null : _shareCoverResolved,
                                icon: const Icon(Icons.ios_share_outlined),
                                label: const Text('Поделиться обложкой'),
                              ),
                              OutlinedButton.icon(
                                onPressed:
                                    (_busyAction || _sendingRequest || _loading) ? null : _copyCoverUrl,
                                icon: const Icon(Icons.link_outlined),
                                label: const Text('Ссылка на обложку'),
                              ),
                            ],
                          ),
                          const SizedBox(height: 14),
                          Card(
                            child: Padding(
                              padding: const EdgeInsets.all(14),
                              child: Text(
                                book.description.isEmpty ? 'Описание отсутствует' : book.description,
                                softWrap: true,
                              ),
                            ),
                          ),
                          const SizedBox(height: 12),
                          Builder(
                            builder: (context) {
                              final fileTypeLabel = details?.fileType.trim();
                              final showFileType = fileTypeLabel != null &&
                                  fileTypeLabel.isNotEmpty &&
                                  fileTypeLabel.toLowerCase() != 'unknown';
                              return Wrap(
                                spacing: 8,
                                runSpacing: 8,
                                children: <Widget>[
                                  _StatusChip(
                                    icon: details?.canRead == true ? Icons.check_circle : Icons.lock_outline,
                                    text: details?.canRead == true ? 'Доступ открыт' : 'Доступ ограничен',
                                    background: details?.canRead == true
                                        ? Colors.green.shade100
                                        : Colors.orange.shade100,
                                    foreground: details?.canRead == true
                                        ? Colors.green.shade900
                                        : Colors.orange.shade900,
                                  ),
                                  if (book.requiresSubscription)
                                    const _StatusChip(
                                      icon: Icons.workspace_premium_outlined,
                                      text: 'Подписочная книга',
                                      background: Color(0xFFFFF2CC),
                                      foreground: AppThemeColors.primaryDark,
                                    ),
                                  if (showFileType)
                                    _StatusChip(
                                      icon: Icons.attach_file_outlined,
                                      text: 'Файл: ${fileTypeLabel.toUpperCase()}',
                                      background: Colors.blueGrey.shade100,
                                      foreground: Colors.blueGrey.shade900,
                                    ),
                                  ...book.categories.take(4).map(
                                        (category) => _StatusChip(
                                          icon: Icons.sell_outlined,
                                          text: category.categoryName,
                                          background: AppThemeColors.primarySoft,
                                          foreground: AppThemeColors.primaryDark,
                                        ),
                                      ),
                                ],
                              );
                            },
                          ),
                          if ((details?.personalRequestStatus ?? '').isNotEmpty) ...<Widget>[
                            const SizedBox(height: 12),
                            Card(
                              child: ListTile(
                                leading: const Icon(Icons.pending_actions_outlined),
                                title: const Text('Статус заявки'),
                                subtitle: Text(details!.personalRequestStatus!),
                              ),
                            ),
                          ],
                          if (details?.canRead == true) ...<Widget>[
                            const SizedBox(height: 14),
                            Card(
                              child: Padding(
                                padding: const EdgeInsets.all(12),
                                child: Column(
                                  crossAxisAlignment: CrossAxisAlignment.stretch,
                                  children: <Widget>[
                                    Text(
                                      'Действия',
                                      style: Theme.of(context)
                                          .textTheme
                                          .titleSmall
                                          ?.copyWith(fontWeight: FontWeight.w800),
                                    ),
                                    const SizedBox(height: 10),
                                    if (book.hasAttachedFile) ...<Widget>[
                                      FilledButton.icon(
                                        style: FilledButton.styleFrom(
                                          backgroundColor: AppThemeColors.primaryMedium,
                                          foregroundColor: Colors.white,
                                        ),
                                        onPressed:
                                            (_busyAction || _sendingRequest || _loading) ? null : _readBookResolved,
                                        icon: const Icon(Icons.chrome_reader_mode_outlined),
                                        label: const Text('Читать в приложении'),
                                      ),
                                      const SizedBox(height: 8),
                                      FilledButton.icon(
                                        style: FilledButton.styleFrom(
                                          backgroundColor: AppThemeColors.primaryDark,
                                          foregroundColor: Colors.white,
                                        ),
                                        onPressed:
                                            (_busyAction || _sendingRequest || _loading) ? null : _saveBookFileResolved,
                                        icon: const Icon(Icons.save_alt_outlined),
                                        label: const Text('Сохранить файл на телефон'),
                                      ),
                                      const SizedBox(height: 8),
                                      OutlinedButton.icon(
                                        onPressed: (_busyAction || _sendingRequest || _loading)
                                            ? null
                                            : _shareBookFileResolved,
                                        icon: const Icon(Icons.share_outlined),
                                        label: const Text('Поделиться файлом'),
                                      ),
                                      const SizedBox(height: 8),
                                    ],
                                    OutlinedButton.icon(
                                      onPressed:
                                          (_busyAction || _sendingRequest || _loading) ? null : _markReadResolved,
                                      icon: const Icon(Icons.auto_stories_outlined),
                                      label: const Text('Отметить как прочитанную'),
                                    ),
                                  ],
                                ),
                              ),
                            ),
                          ],
                          if (book.requiresSubscription && details?.canRead != true) ...<Widget>[
                            const SizedBox(height: 16),
                            SizedBox(
                              width: double.infinity,
                              child: FilledButton(
                                style: FilledButton.styleFrom(
                                  backgroundColor: AppThemeColors.accentGold,
                                  foregroundColor: AppThemeColors.primaryDark,
                                ),
                                onPressed: _sendingRequest ? null : _sendBookRequest,
                                child: _sendingRequest
                                    ? const SizedBox.square(
                                        dimension: 18,
                                        child: CircularProgressIndicator(strokeWidth: 2),
                                      )
                                    : const Text('Отправить заявку на доступ'),
                              ),
                            ),
                          ],
                        ],
                      ),
      ),
    );
  }
}

class _StatusChip extends StatelessWidget {
  const _StatusChip({
    required this.icon,
    required this.text,
    required this.background,
    required this.foreground,
  });

  final IconData icon;
  final String text;
  final Color background;
  final Color foreground;

  @override
  Widget build(BuildContext context) {
    final maxW = MediaQuery.sizeOf(context).width - 40;
    return ConstrainedBox(
      constraints: BoxConstraints(maxWidth: maxW.clamp(0, 600)),
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
        decoration: BoxDecoration(
          color: background,
          borderRadius: BorderRadius.circular(999),
        ),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            Icon(icon, size: 16, color: foreground),
            const SizedBox(width: 6),
            Expanded(
              child: Text(
                text,
                maxLines: 3,
                overflow: TextOverflow.ellipsis,
                softWrap: true,
                style: TextStyle(color: foreground, fontWeight: FontWeight.w700, fontSize: 12.5),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
