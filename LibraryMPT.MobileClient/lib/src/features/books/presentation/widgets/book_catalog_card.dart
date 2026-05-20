import 'package:flutter/material.dart';

import '../../../../core/network/api_client.dart';
import '../../../../core/theme/app_theme.dart';
import '../../../common/models.dart';
import 'book_cover_image.dart';

class BookCatalogCard extends StatelessWidget {
  const BookCatalogCard({
    required this.book,
    required this.apiClient,
    required this.hasSubscription,
    required this.readedBookIds,
    required this.personalPendingBookIds,
    required this.personalApprovedBookIds,
    required this.onOpenDetails,
    required this.onMarkRead,
    required this.onSaveBookFile,
    required this.onShareBookFile,
    required this.onShareCover,
    required this.onSaveCoverToGallery,
    super.key,
  });

  final BookItem book;
  final ApiClient apiClient;
  final bool hasSubscription;
  final List<int> readedBookIds;
  final List<int> personalPendingBookIds;
  final List<int> personalApprovedBookIds;
  final VoidCallback onOpenDetails;
  final Future<void> Function() onMarkRead;
  final Future<void> Function() onSaveBookFile;
  final Future<void> Function() onShareBookFile;
  final Future<void> Function() onShareCover;
  final Future<void> Function() onSaveCoverToGallery;

  bool get _isRead => readedBookIds.contains(book.bookId);
  bool get _personalPending =>
      personalPendingBookIds.contains(book.bookId) && book.requiresSubscription && !hasSubscription;
  bool get _personalApproved => personalApprovedBookIds.contains(book.bookId);
  bool get _canAccessFile =>
      !book.requiresSubscription || hasSubscription || _personalApproved;

  @override
  Widget build(BuildContext context) {
    final categoriesLine = book.categories.isEmpty
        ? null
        : book.categories.map((c) => c.categoryName).join(', ');

    return Card(
      margin: const EdgeInsets.only(bottom: 14),
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: onOpenDetails,
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: <Widget>[
              Center(
                child: Container(
                  constraints: const BoxConstraints(maxWidth: 192),
                  padding: const EdgeInsets.all(10),
                  decoration: BoxDecoration(
                    color: Colors.white,
                    borderRadius: BorderRadius.circular(20),
                    boxShadow: <BoxShadow>[
                      BoxShadow(
                        color: AppThemeColors.primaryDark.withValues(alpha: 0.1),
                        blurRadius: 16,
                        offset: const Offset(0, 7),
                      ),
                    ],
                  ),
                  child: AspectRatio(
                    aspectRatio: 2 / 3,
                    child: Stack(
                      fit: StackFit.expand,
                      children: <Widget>[
                        Positioned.fill(
                          child: BookCoverImage(
                            bookId: book.bookId,
                            imagePath: book.imagePath,
                            apiClient: apiClient,
                            fillParent: true,
                            fit: BoxFit.contain,
                            borderRadius: 12,
                            padding: const EdgeInsets.all(4),
                          ),
                        ),
                        Positioned(
                          top: 6,
                          right: 6,
                          child: Column(
                            mainAxisSize: MainAxisSize.min,
                            children: <Widget>[
                              Material(
                                color: Colors.black54,
                                borderRadius: BorderRadius.circular(999),
                                child: InkWell(
                                  onTap: () => onSaveCoverToGallery(),
                                  borderRadius: BorderRadius.circular(999),
                                  child: const Padding(
                                    padding: EdgeInsets.all(6),
                                    child:
                                        Icon(Icons.photo_library_outlined, color: Colors.white, size: 18),
                                  ),
                                ),
                              ),
                              const SizedBox(height: 6),
                              Material(
                                color: Colors.black54,
                                borderRadius: BorderRadius.circular(999),
                                child: InkWell(
                                  onTap: () => onShareCover(),
                                  borderRadius: BorderRadius.circular(999),
                                  child: const Padding(
                                    padding: EdgeInsets.all(6),
                                    child: Icon(Icons.ios_share_outlined, color: Colors.white, size: 18),
                                  ),
                                ),
                              ),
                            ],
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
              ),
              const SizedBox(height: 14),
              Text(
                book.title,
                maxLines: 4,
                overflow: TextOverflow.ellipsis,
                textAlign: TextAlign.center,
                style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 18.5, height: 1.25),
              ),
              const SizedBox(height: 10),
              Row(
                children: <Widget>[
                  const Icon(Icons.person_outline, size: 16, color: AppThemeColors.textLight),
                  const SizedBox(width: 6),
                  Expanded(
                    child: Text(
                      book.author?.fullName.isNotEmpty == true
                          ? book.author!.fullName
                          : 'Автор не указан',
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(
                        color: AppThemeColors.textLight,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                  ),
                ],
              ),
              if (book.publisher?.name.isNotEmpty == true) ...<Widget>[
                const SizedBox(height: 6),
                Row(
                  children: <Widget>[
                    const Icon(Icons.business_outlined, size: 16, color: AppThemeColors.textLight),
                    const SizedBox(width: 6),
                    Expanded(
                      child: Text(
                        book.publisher!.name,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: const TextStyle(color: AppThemeColors.textLight, fontSize: 13),
                      ),
                    ),
                  ],
                ),
              ],
              if (categoriesLine != null) ...<Widget>[
                const SizedBox(height: 8),
                Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: <Widget>[
                    const Padding(
                      padding: EdgeInsets.only(top: 2),
                      child: Icon(Icons.sell_outlined, size: 16, color: AppThemeColors.primaryMedium),
                    ),
                    const SizedBox(width: 6),
                    Expanded(
                      child: Text(
                        categoriesLine,
                        maxLines: 3,
                        overflow: TextOverflow.ellipsis,
                        style: const TextStyle(
                          color: AppThemeColors.primaryDark,
                          fontWeight: FontWeight.w600,
                          fontSize: 13,
                        ),
                      ),
                    ),
                  ],
                ),
              ],
              if (book.publishYear != null) ...<Widget>[
                const SizedBox(height: 6),
                Row(
                  children: <Widget>[
                    const Icon(Icons.calendar_today_outlined, size: 14, color: AppThemeColors.textLight),
                    const SizedBox(width: 6),
                    Text(
                      '${book.publishYear}',
                      style: const TextStyle(color: AppThemeColors.textLight, fontSize: 13),
                    ),
                  ],
                ),
              ],
              const Divider(height: 22),
              Text(
                book.description.isEmpty ? 'Описание отсутствует' : book.description,
                maxLines: 4,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(
                  color: Colors.grey.shade800,
                  height: 1.35,
                  fontSize: 13.5,
                ),
              ),
              const SizedBox(height: 12),
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: <Widget>[
                  _GradientPill(
                    gradient: book.requiresSubscription ? AppThemeColors.goldGradient : AppThemeColors.primaryGradient,
                    foreground: book.requiresSubscription ? AppThemeColors.primaryDark : Colors.white,
                    label: book.requiresSubscription ? 'Подписка' : 'Свободно',
                  ),
                  if (_isRead)
                    const _SoftPill(icon: Icons.check_circle, label: 'Прочитано', color: Colors.green),
                  if (_personalPending)
                    const _SoftPill(
                      icon: Icons.schedule,
                      label: 'Заявка на рассмотрении',
                      color: Colors.orange,
                    ),
                ],
              ),
              const SizedBox(height: 12),
              Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: <Widget>[
                  FilledButton.icon(
                    style: FilledButton.styleFrom(
                      backgroundColor: AppThemeColors.primaryDark,
                      foregroundColor: Colors.white,
                    ),
                    onPressed: onOpenDetails,
                    icon: const Icon(Icons.visibility_outlined, size: 20),
                    label: Text(
                      'Подробнее',
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      textAlign: TextAlign.center,
                    ),
                  ),
                  const SizedBox(height: 8),
                  if (_canAccessFile && book.hasAttachedFile && !_isRead)
                    OutlinedButton.icon(
                      onPressed: () => onMarkRead(),
                      icon: const Icon(Icons.check_circle_outline),
                      label: Text(
                        'Отметить как прочитанную',
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                        textAlign: TextAlign.center,
                      ),
                    ),
                  if (_canAccessFile && book.hasAttachedFile && !_isRead) const SizedBox(height: 8),
                  if (book.hasAttachedFile)
                    _canAccessFile
                        ? Column(
                            crossAxisAlignment: CrossAxisAlignment.stretch,
                            children: <Widget>[
                              FilledButton.icon(
                                style: FilledButton.styleFrom(
                                  backgroundColor: AppThemeColors.primaryDark,
                                  foregroundColor: Colors.white,
                                ),
                                onPressed: () => onSaveBookFile(),
                                icon: const Icon(Icons.save_alt_outlined),
                                label: Text(
                                  'Сохранить файл на телефон',
                                  maxLines: 2,
                                  overflow: TextOverflow.ellipsis,
                                  textAlign: TextAlign.center,
                                ),
                              ),
                              const SizedBox(height: 8),
                              OutlinedButton.icon(
                                onPressed: () => onShareBookFile(),
                                icon: const Icon(Icons.share_outlined),
                                label: Text(
                                  'Поделиться файлом',
                                  maxLines: 2,
                                  overflow: TextOverflow.ellipsis,
                                  textAlign: TextAlign.center,
                                ),
                              ),
                            ],
                          )
                        : OutlinedButton.icon(
                            onPressed: null,
                            icon: const Icon(Icons.lock_outline),
                            label: Text(
                              _personalPending ? 'Доступ после одобрения заявки' : 'Доступ по подписке',
                              maxLines: 2,
                              overflow: TextOverflow.ellipsis,
                              textAlign: TextAlign.center,
                            ),
                          )
                  else
                    OutlinedButton.icon(
                      onPressed: null,
                      icon: const Icon(Icons.file_open_outlined),
                      label: Text(
                        'Нет файла',
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        textAlign: TextAlign.center,
                      ),
                    ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _GradientPill extends StatelessWidget {
  const _GradientPill({
    required this.gradient,
    required this.foreground,
    required this.label,
  });

  final LinearGradient gradient;
  final Color foreground;
  final String label;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
      decoration: BoxDecoration(
        gradient: gradient,
        borderRadius: BorderRadius.circular(999),
      ),
      child: Text(
        label,
        style: TextStyle(color: foreground, fontWeight: FontWeight.w700, fontSize: 12),
      ),
    );
  }
}

class _SoftPill extends StatelessWidget {
  const _SoftPill({
    required this.icon,
    required this.label,
    required this.color,
  });

  final IconData icon;
  final String label;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.15),
        borderRadius: BorderRadius.circular(999),
      ),
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 260),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            Icon(icon, size: 16, color: color),
            const SizedBox(width: 6),
            Expanded(
              child: Text(
                label,
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(color: color, fontWeight: FontWeight.w700, fontSize: 12),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
