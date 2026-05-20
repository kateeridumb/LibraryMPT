import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../../core/network/api_client.dart';
import '../../../core/network/api_exception.dart';
import '../../../core/theme/app_theme.dart';
import '../../common/models.dart';
import '../data/books_repository.dart';
import 'book_details_screen.dart';
import 'widgets/book_catalog_card.dart';

class BooksScreen extends StatefulWidget {
  const BooksScreen({super.key});

  @override
  State<BooksScreen> createState() => _BooksScreenState();
}

class _BooksScreenState extends State<BooksScreen> {
  final _searchController = TextEditingController();
  bool _loading = true;
  String? _error;
  ClientIndexData? _data;
  String _accessFilter = 'all';
  final Set<int> _selectedCategoryIds = <int>{};

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _searchController.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final data = await context.read<BooksRepository>().loadBooks(
            search: _searchController.text,
            categoryIds:
                _selectedCategoryIds.isEmpty ? null : _selectedCategoryIds.toList(),
          );
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
        _error = 'Не удалось загрузить каталог.';
      });
    } finally {
      if (mounted) {
        setState(() {
          _loading = false;
        });
      }
    }
  }

  void _toggleCategory(int categoryId) {
    setState(() {
      if (_selectedCategoryIds.contains(categoryId)) {
        _selectedCategoryIds.remove(categoryId);
      } else {
        _selectedCategoryIds.add(categoryId);
      }
    });
    _load();
  }

  void _clearCategories() {
    if (_selectedCategoryIds.isEmpty) {
      return;
    }
    setState(_selectedCategoryIds.clear);
    _load();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Каталог'),
        actions: <Widget>[
          IconButton(
            onPressed: _loading ? null : _load,
            icon: const Icon(Icons.refresh),
          ),
        ],
      ),
      body: SafeArea(
        child: Column(
          children: <Widget>[
            Container(
              margin: const EdgeInsets.fromLTRB(12, 2, 12, 12),
              padding: const EdgeInsets.all(14),
              decoration: BoxDecoration(
                gradient: AppThemeColors.primaryGradient,
                borderRadius: BorderRadius.circular(18),
                boxShadow: const <BoxShadow>[
                  BoxShadow(
                    color: Color(0x24044857),
                    blurRadius: 20,
                    offset: Offset(0, 10),
                  ),
                ],
              ),
              child: Row(
                children: <Widget>[
                  Expanded(
                    child: TextField(
                      controller: _searchController,
                      style: const TextStyle(color: AppThemeColors.textDark),
                      decoration: const InputDecoration(
                        hintText: 'Поиск по названию и описанию',
                        prefixIcon: Icon(Icons.search),
                        fillColor: Colors.white,
                      ),
                    ),
                  ),
                  const SizedBox(width: 8),
                  FilledButton(
                    style: FilledButton.styleFrom(
                      backgroundColor: AppThemeColors.accentGold,
                      foregroundColor: AppThemeColors.primaryDark,
                    ),
                    onPressed: _loading ? null : _load,
                    child: const Text('Найти'),
                  ),
                ],
              ),
            ),
            if (_loading)
              const Expanded(
                child: Center(child: CircularProgressIndicator()),
              )
            else if (_error != null)
              Expanded(
                child: Center(child: Text(_error!)),
              )
            else
              Expanded(
                child: _BooksList(
                  data: _data ??
                      ClientIndexData(
                        books: const <BookItem>[],
                        categories: const <BookCategoryItem>[],
                        totalBooks: 0,
                        hasSubscription: false,
                        subscriptionStatus: '',
                        readedBookIds: const <int>[],
                        personalPendingBookIds: const <int>[],
                        personalApprovedBookIds: const <int>[],
                        readedBooksCount: 0,
                      ),
                  accessFilter: _accessFilter,
                  selectedCategoryIds: _selectedCategoryIds,
                  onAccessFilterChanged: (value) {
                    setState(() {
                      _accessFilter = value;
                    });
                  },
                  onToggleCategory: _toggleCategory,
                  onClearCategories: _clearCategories,
                  onReload: _load,
                ),
              ),
          ],
        ),
      ),
    );
  }
}

class _BooksList extends StatelessWidget {
  const _BooksList({
    required this.data,
    required this.accessFilter,
    required this.selectedCategoryIds,
    required this.onAccessFilterChanged,
    required this.onToggleCategory,
    required this.onClearCategories,
    required this.onReload,
  });

  final ClientIndexData data;
  final String accessFilter;
  final Set<int> selectedCategoryIds;
  final ValueChanged<String> onAccessFilterChanged;
  final ValueChanged<int> onToggleCategory;
  final VoidCallback onClearCategories;
  final Future<void> Function() onReload;

  Future<void> _markRead(BuildContext context, BookItem book) async {
    try {
      await context.read<BooksRepository>().markBookRead(book.bookId);
      if (!context.mounted) {
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Книга отмечена как прочитанная.')),
      );
      await onReload();
    } on ApiException catch (e) {
      if (!context.mounted) {
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
    }
  }

  Future<void> _saveBookOnDevice(BuildContext context, BookItem book) async {
    try {
      final hint = await context.read<BooksRepository>().saveBookFileOnDevice(book.bookId);
      if (!context.mounted) {
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(hint)));
    } on ApiException catch (e) {
      if (!context.mounted) {
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
    }
  }

  Future<void> _shareBookFile(BuildContext context, BookItem book) async {
    try {
      final name = await context.read<BooksRepository>().fetchAndShareBookDownload(book.bookId);
      if (!context.mounted) {
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Выберите приложение для сохранения: $name')),
      );
    } on ApiException catch (e) {
      if (!context.mounted) {
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
    }
  }

  Future<void> _shareCover(BuildContext context, BookItem book) async {
    try {
      await context.read<BooksRepository>().shareCoverImage(
            book.bookId,
            book.title,
            imagePath: book.imagePath,
          );
    } on ApiException catch (e) {
      if (!context.mounted) {
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
    }
  }

  Future<void> _saveCoverToGallery(BuildContext context, BookItem book) async {
    try {
      final msg =
          await context.read<BooksRepository>().saveCoverToGallery(book.bookId, imagePath: book.imagePath);
      if (!context.mounted) {
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(msg)));
    } on ApiException catch (e) {
      if (!context.mounted) {
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
    }
  }

  @override
  Widget build(BuildContext context) {
    final visibleBooks = data.books.where((book) {
      if (accessFilter == 'subscription') {
        return book.requiresSubscription;
      }
      if (accessFilter == 'free') {
        return !book.requiresSubscription;
      }
      return true;
    }).toList();

    if (visibleBooks.isEmpty) {
      return const Center(child: Text('Книги не найдены.'));
    }

    final apiClient = context.read<ApiClient>();

    final categoryLine = SizedBox(
      height: 40,
      child: ListView(
        scrollDirection: Axis.horizontal,
        padding: const EdgeInsets.symmetric(horizontal: 14),
        children: <Widget>[
          Padding(
            padding: const EdgeInsets.only(right: 8),
            child: FilterChip(
              label: const Text('Все темы'),
              selected: selectedCategoryIds.isEmpty,
              onSelected: (_) => onClearCategories(),
            ),
          ),
          ...data.categories.map((c) {
            final selected = selectedCategoryIds.contains(c.categoryId);
            return Padding(
              padding: const EdgeInsets.only(right: 8),
              child: FilterChip(
                label: ConstrainedBox(
                  constraints: const BoxConstraints(maxWidth: 220),
                  child: Text(
                    c.categoryName,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
                selected: selected,
                onSelected: (_) => onToggleCategory(c.categoryId),
              ),
            );
          }),
        ],
      ),
    );

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 14),
          child: SingleChildScrollView(
            scrollDirection: Axis.horizontal,
            child: Row(
              children: <Widget>[
                _FilterChip(
                  label: 'Все',
                  selected: accessFilter == 'all',
                  onTap: () => onAccessFilterChanged('all'),
                ),
                const SizedBox(width: 8),
                _FilterChip(
                  label: 'Подписка',
                  selected: accessFilter == 'subscription',
                  onTap: () => onAccessFilterChanged('subscription'),
                ),
                const SizedBox(width: 8),
                _FilterChip(
                  label: 'Свободные',
                  selected: accessFilter == 'free',
                  onTap: () => onAccessFilterChanged('free'),
                ),
              ],
            ),
          ),
        ),
        if (data.categories.isNotEmpty) ...<Widget>[
          const SizedBox(height: 6),
          categoryLine,
        ],
        const SizedBox(height: 10),
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 4),
          child: Wrap(
            spacing: 8,
            runSpacing: 8,
            children: <Widget>[
              _SummaryPill(
                icon: Icons.library_books_outlined,
                text: 'В каталоге: ${data.totalBooks}',
              ),
              _SummaryPill(
                icon: data.hasSubscription ? Icons.verified_outlined : Icons.warning_amber_outlined,
                text: data.hasSubscription
                    ? (data.subscriptionStatus.isNotEmpty ? data.subscriptionStatus : 'Подписка активна')
                    : 'Без подписки',
              ),
              _SummaryPill(
                icon: Icons.auto_stories_outlined,
                text: 'Прочитано: ${data.readedBooksCount}',
              ),
            ],
          ),
        ),
        Expanded(
          child: ListView.builder(
            padding: const EdgeInsets.fromLTRB(12, 8, 12, 24),
            itemCount: visibleBooks.length,
            itemBuilder: (context, index) {
              final book = visibleBooks[index];
              return BookCatalogCard(
                book: book,
                apiClient: apiClient,
                hasSubscription: data.hasSubscription,
                readedBookIds: data.readedBookIds,
                personalPendingBookIds: data.personalPendingBookIds,
                personalApprovedBookIds: data.personalApprovedBookIds,
                onOpenDetails: () {
                  Navigator.of(context).push(
                    MaterialPageRoute<void>(
                      builder: (_) => BookDetailsScreen(bookId: book.bookId),
                    ),
                  );
                },
                onMarkRead: () => _markRead(context, book),
                onSaveBookFile: () => _saveBookOnDevice(context, book),
                onShareBookFile: () => _shareBookFile(context, book),
                onShareCover: () => _shareCover(context, book),
                onSaveCoverToGallery: () => _saveCoverToGallery(context, book),
              );
            },
          ),
        ),
      ],
    );
  }
}

class _FilterChip extends StatelessWidget {
  const _FilterChip({
    required this.label,
    required this.selected,
    required this.onTap,
  });

  final String label;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTap: onTap,
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 7),
        decoration: BoxDecoration(
          gradient: selected ? AppThemeColors.primaryGradient : null,
          color: selected ? null : Colors.white,
          borderRadius: BorderRadius.circular(999),
          border: Border.all(color: AppThemeColors.primarySoft),
        ),
        child: Text(
          label,
          style: TextStyle(
            color: selected ? Colors.white : AppThemeColors.primaryDark,
            fontWeight: FontWeight.w700,
            fontSize: 12.5,
          ),
        ),
      ),
    );
  }
}

class _SummaryPill extends StatelessWidget {
  const _SummaryPill({required this.icon, required this.text});

  final IconData icon;
  final String text;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
      decoration: BoxDecoration(
        color: Colors.white,
        borderRadius: BorderRadius.circular(999),
        border: Border.all(color: AppThemeColors.primarySoft),
      ),
      child: Row(
        children: <Widget>[
          Icon(icon, size: 14, color: AppThemeColors.primaryMedium),
          const SizedBox(width: 5),
          Flexible(
            child: Text(
              text,
              style: const TextStyle(fontSize: 12.5, fontWeight: FontWeight.w700),
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
            ),
          ),
        ],
      ),
    );
  }
}
