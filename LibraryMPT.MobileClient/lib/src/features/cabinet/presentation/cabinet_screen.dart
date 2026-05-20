import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../../core/network/api_client.dart';
import '../../../core/network/api_exception.dart';
import '../../../core/theme/app_theme.dart';
import '../../common/models.dart';
import '../../books/presentation/book_details_screen.dart';
import '../../books/presentation/widgets/book_cover_image.dart';
import '../data/cabinet_repository.dart';

class CabinetScreen extends StatefulWidget {
  const CabinetScreen({super.key});

  @override
  State<CabinetScreen> createState() => _CabinetScreenState();
}

class _CabinetScreenState extends State<CabinetScreen> {
  bool _loading = true;
  String? _error;
  CabinetData? _data;

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
      final data = await context.read<CabinetRepository>().loadCabinet();
      if (!mounted) {
        return;
      }
      setState(() {
        _data = data;
      });
    } on ApiException catch (error) {
      setState(() {
        _error = error.message;
      });
    } catch (_) {
      setState(() {
        _error = 'Не удалось загрузить кабинет.';
      });
    } finally {
      if (mounted) {
        setState(() {
          _loading = false;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Личный кабинет'),
        actions: <Widget>[
          IconButton(
            onPressed: _loading ? null : _load,
            icon: const Icon(Icons.refresh),
          ),
        ],
      ),
      body: SafeArea(
        child: _loading
            ? const Center(child: CircularProgressIndicator())
            : _error != null
                ? Center(child: Text(_error!))
                : _CabinetBody(data: _data ?? CabinetData(readedBooks: const <BookItem>[], requestableBooks: const <BookItem>[])),
      ),
    );
  }
}

class _CabinetBody extends StatelessWidget {
  const _CabinetBody({required this.data});

  final CabinetData data;

  @override
  Widget build(BuildContext context) {
    final readed = data.readedBooks;
    final requestable = data.requestableBooks;
    final apiClient = context.read<ApiClient>();

    return ListView(
      padding: const EdgeInsets.fromLTRB(14, 8, 14, 22),
      children: <Widget>[
        Container(
          padding: const EdgeInsets.all(16),
          decoration: BoxDecoration(
            gradient: AppThemeColors.primaryGradient,
            borderRadius: BorderRadius.circular(20),
          ),
          child: Row(
            children: <Widget>[
              Expanded(
                child: _StatTile(
                  icon: Icons.check_circle_outline,
                  label: 'Прочитано',
                  value: readed.length.toString(),
                ),
              ),
              const SizedBox(width: 10),
              Expanded(
                child: _StatTile(
                  icon: Icons.pending_actions_outlined,
                  label: 'Доступ по заявке',
                  value: requestable.length.toString(),
                ),
              ),
            ],
          ),
        ),
        const SizedBox(height: 14),
        Text(
          'Недавно прочитанные',
          style: Theme.of(context).textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w700),
        ),
        const SizedBox(height: 8),
        if (readed.isEmpty)
          const _EmptyState(text: 'Пока нет прочитанных книг')
        else
          ...readed.take(8).map(
                (book) => Card(
                  margin: const EdgeInsets.only(bottom: 8),
                  child: InkWell(
                    onTap: () {
                      Navigator.of(context).push(
                        MaterialPageRoute<void>(
                          builder: (_) => BookDetailsScreen(bookId: book.bookId),
                        ),
                      );
                    },
                    borderRadius: BorderRadius.circular(12),
                    child: Padding(
                      padding: const EdgeInsets.all(10),
                      child: Row(
                        children: <Widget>[
                          SizedBox(
                            width: 58,
                            child: AspectRatio(
                              aspectRatio: 2 / 3,
                              child: BookCoverImage(
                                bookId: book.bookId,
                                imagePath: book.imagePath,
                                apiClient: apiClient,
                                fillParent: true,
                                fit: BoxFit.contain,
                                borderRadius: 10,
                                padding: const EdgeInsets.all(2),
                              ),
                            ),
                          ),
                          const SizedBox(width: 10),
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: <Widget>[
                                Text(book.title, maxLines: 1, overflow: TextOverflow.ellipsis),
                                const SizedBox(height: 4),
                                Text(
                                  book.author?.fullName.isNotEmpty == true
                                      ? book.author!.fullName
                                      : 'Автор не указан',
                                  maxLines: 2,
                                  overflow: TextOverflow.ellipsis,
                                  style: const TextStyle(color: AppThemeColors.textLight),
                                ),
                              ],
                            ),
                          ),
                          const Icon(Icons.chevron_right, color: AppThemeColors.textLight),
                          const SizedBox(width: 4),
                          const Icon(Icons.done_all, color: Colors.green),
                        ],
                      ),
                    ),
                  ),
                ),
              ),
        const SizedBox(height: 10),
        Text(
          'Можно запросить доступ',
          style: Theme.of(context).textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w700),
        ),
        const SizedBox(height: 8),
        if (requestable.isEmpty)
          const _EmptyState(text: 'Сейчас нет доступных книг для заявки')
        else
          ...requestable.take(8).map(
                (book) => Card(
                  margin: const EdgeInsets.only(bottom: 8),
                  child: InkWell(
                    onTap: () {
                      Navigator.of(context).push(
                        MaterialPageRoute<void>(
                          builder: (_) => BookDetailsScreen(bookId: book.bookId),
                        ),
                      );
                    },
                    borderRadius: BorderRadius.circular(12),
                    child: Padding(
                      padding: const EdgeInsets.all(10),
                      child: Row(
                        children: <Widget>[
                          SizedBox(
                            width: 58,
                            child: AspectRatio(
                              aspectRatio: 2 / 3,
                              child: BookCoverImage(
                                bookId: book.bookId,
                                imagePath: book.imagePath,
                                apiClient: apiClient,
                                fillParent: true,
                                fit: BoxFit.contain,
                                borderRadius: 10,
                                padding: const EdgeInsets.all(2),
                              ),
                            ),
                          ),
                          const SizedBox(width: 10),
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: <Widget>[
                                Text(book.title, maxLines: 1, overflow: TextOverflow.ellipsis),
                                const SizedBox(height: 4),
                                if (book.categories.isNotEmpty)
                                  Text(
                                    book.categories.first.categoryName,
                                    maxLines: 2,
                                    overflow: TextOverflow.ellipsis,
                                    style: const TextStyle(color: AppThemeColors.textLight),
                                  ),
                              ],
                            ),
                          ),
                          const Icon(Icons.lock_outline, color: AppThemeColors.accentGold, size: 20),
                          const Icon(Icons.chevron_right, color: AppThemeColors.textLight),
                        ],
                      ),
                    ),
                  ),
                ),
              ),
      ],
    );
  }
}

class _StatTile extends StatelessWidget {
  const _StatTile({
    required this.icon,
    required this.label,
    required this.value,
  });

  final IconData icon;
  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: Colors.white.withValues(alpha: 0.12),
        borderRadius: BorderRadius.circular(14),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Icon(icon, color: Colors.white),
          const SizedBox(height: 10),
          Text(
            value,
            style: const TextStyle(color: Colors.white, fontSize: 24, fontWeight: FontWeight.w800),
          ),
          Text(
            label,
            style: const TextStyle(color: Colors.white70, fontSize: 12),
          ),
        ],
      ),
    );
  }
}

class _EmptyState extends StatelessWidget {
  const _EmptyState({required this.text});

  final String text;

  @override
  Widget build(BuildContext context) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Row(
          children: <Widget>[
            const Icon(Icons.info_outline, color: AppThemeColors.textLight),
            const SizedBox(width: 8),
            Expanded(child: Text(text)),
          ],
        ),
      ),
    );
  }
}
