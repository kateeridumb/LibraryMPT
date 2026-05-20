import 'package:flutter/material.dart';

import '../../../../core/network/api_client.dart';
import '../../../../core/theme/app_theme.dart';
import '../../../../core/utils/book_media_urls.dart';

class BookCoverImage extends StatelessWidget {
  const BookCoverImage({
    required this.bookId,
    required this.apiClient,
    this.imagePath,
    this.fit = BoxFit.cover,
    this.borderRadius = 14,
    this.fillParent = false,
    this.height = 170,
    this.width,
    this.padding = EdgeInsets.zero,
    super.key,
  });

  final int bookId;
  final String? imagePath;
  final ApiClient apiClient;
  final BoxFit fit;
  final double borderRadius;

  /// Когда родитель задаёт явный слот ([AspectRatio] и т.д.) — включается [SizedBox.expand].
  final bool fillParent;
  /// Используется только если `fillParent == false`.
  final double height;
  final double? width;
  final EdgeInsetsGeometry padding;

  @override
  Widget build(BuildContext context) {
    final url = BookMediaUrls.displayCoverUrl(bookId: bookId, imagePath: imagePath);
    final needsAuth = BookMediaUrls.displayCoverNeedsAuthorization(imagePath);
    final token = apiClient.accessToken;
    final headers = !needsAuth || token == null || token.isEmpty
        ? const <String, String>{}
        : <String, String>{'Authorization': 'Bearer $token'};

    final Widget imageLayer = Container(
      color: AppThemeColors.primarySoft,
      alignment: Alignment.center,
      padding: padding,
      child: Image.network(
        url,
        headers: headers,
        fit: fit,
        filterQuality: FilterQuality.medium,
        errorBuilder: (_, __, ___) => const _CoverPlaceholder(),
        loadingBuilder: (context, child, event) {
          if (event == null) {
            return child;
          }
          return const Center(child: CircularProgressIndicator(strokeWidth: 2));
        },
      ),
    );

    final Widget constrained = fillParent
        ? SizedBox.expand(child: imageLayer)
        : SizedBox(
            height: height,
            width: width ?? double.infinity,
            child: imageLayer,
          );

    return ClipRRect(
      borderRadius: BorderRadius.circular(borderRadius),
      child: constrained,
    );
  }
}

class _CoverPlaceholder extends StatelessWidget {
  const _CoverPlaceholder();

  @override
  Widget build(BuildContext context) {
    return const Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: <Widget>[
          Icon(Icons.menu_book_outlined, color: AppThemeColors.primaryMedium, size: 30),
          SizedBox(height: 6),
          Text(
            'Нет обложки',
            style: TextStyle(color: AppThemeColors.textLight, fontWeight: FontWeight.w600),
          ),
        ],
      ),
    );
  }
}
