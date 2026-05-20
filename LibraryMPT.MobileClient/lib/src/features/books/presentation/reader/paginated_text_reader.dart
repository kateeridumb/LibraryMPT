import 'dart:async' show Timer, unawaited;
import 'dart:convert';
import 'dart:math' as math;

import 'package:flutter/foundation.dart' show debugPrint;
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../../../core/network/api_exception.dart';
import '../../../../core/theme/app_theme.dart';
import '../../data/books_repository.dart';
import '../../data/reader_api_models.dart';
import '../../data/text_pagination.dart';
import 'reader_prefs.dart';

/// Читалка текста с листами, темой, шрифтом, заметками и синхронизацией прогресса.
class PaginatedTextReader extends StatefulWidget {
  const PaginatedTextReader({
    required this.fullText,
    required this.title,
    this.bookId,
    super.key,
  });

  final String fullText;
  final String title;
  final int? bookId;

  @override
  State<PaginatedTextReader> createState() => _PaginatedTextReaderState();
}

class _PaginatedTextReaderState extends State<PaginatedTextReader> {
  final PageController _pageController = PageController();

  bool _prefsReady = false;
  ReaderThemeMode _theme = ReaderThemeMode.light;
  String _fontKey = 'system';
  double _fontSize = 17;
  /// Значение ползунка в листе настроек; до применения читалка использует [_fontSize].
  double _sheetFontSize = 17;

  List<String> _pages = <String>[''];
  int _current = 0;
  /// Пока false — не показываем «пустую» 1/1 страницу до первой удачной верстки.
  bool _paginationReady = false;

  String? _layoutSig;
  /// Счётчик инвалидации вёрстки (текст / шрифт / кегль). Смена только цветовой темы его не трогает.
  int _paginateSeq = 0;
  /// Не ставить в очередь несколько post-frame с одинаковой сигнатурой до их выполнения.
  String? _paginationScheduledForSig;
  Timer? _fontSizeDebounce;
  Timer? _saveDebounce;

  List<BookmarkApi> _bookmarks = <BookmarkApi>[];
  bool _syncLoading = false;
  bool _didJumpInitial = false;

  @override
  void initState() {
    super.initState();
    if (widget.fullText.isEmpty) {
      _paginationReady = true;
    }
    _loadPrefs();
  }

  @override
  void didUpdateWidget(PaginatedTextReader oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.fullText != widget.fullText) {
      _layoutSig = null;
      _paginationScheduledForSig = null;
      _paginateSeq++;
      _paginationReady = false;
    }
  }

  @override
  void dispose() {
    _paginateSeq++;
    _fontSizeDebounce?.cancel();
    _saveDebounce?.cancel();
    if (widget.bookId != null) {
      unawaited(_flushProgress());
    }
    _pageController.dispose();
    super.dispose();
  }

  Future<void> _loadPrefs() async {
    final theme = await ReaderPrefs.loadTheme();
    final font = await ReaderPrefs.loadFontKey();
    final size = await ReaderPrefs.loadFontSize();
    if (!mounted) {
      return;
    }
    setState(() {
      _theme = theme;
      _fontKey = font;
      _fontSize = size;
      _sheetFontSize = size;
      _prefsReady = true;
    });
  }

  TextStyle _bodyStyle(Color fg) {
    String? ff;
    switch (_fontKey) {
      case 'serif':
        ff = 'serif';
        break;
      case 'mono':
        ff = 'monospace';
        break;
      default:
        ff = null;
    }
    return TextStyle(
      color: fg,
      fontSize: _fontSize,
      height: 1.5,
      fontFamily: ff,
    );
  }

  ({Color bg, Color fg, Color appBarFg}) _themeColors() {
    switch (_theme) {
      case ReaderThemeMode.sepia:
        return (
          bg: const Color(0xFFF4ECD8),
          fg: const Color(0xFF3B3026),
          appBarFg: const Color(0xFF3B3026),
        );
      case ReaderThemeMode.dark:
        return (
          bg: const Color(0xFF121416),
          fg: const Color(0xFFE8E8E8),
          appBarFg: const Color(0xFFE8E8E8),
        );
      case ReaderThemeMode.light:
        return (
          bg: AppThemeColors.neutralWhite,
          fg: AppThemeColors.textDark,
          appBarFg: AppThemeColors.primaryDark,
        );
    }
  }

  /// Цвет читалки не влияет на переносы — полную пагинацию не трогаем.
  void _cycleTheme() {
    setState(() {
      _theme = ReaderThemeMode.values[(_theme.index + 1) % ReaderThemeMode.values.length];
    });
    ReaderPrefs.saveTheme(_theme);
  }

  void _invalidatePaginationLayout() {
    if (!mounted) {
      return;
    }
    setState(() => _paginationReady = false);
    _layoutSig = null;
    _paginationScheduledForSig = null;
    _paginateSeq++;
  }

  Future<void> _showAppearanceSheet() async {
    _sheetFontSize = _fontSize;
    await showModalBottomSheet<void>(
      context: context,
      showDragHandle: true,
      builder: (ctx) {
        return Padding(
          padding: const EdgeInsets.fromLTRB(8, 0, 8, 24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: <Widget>[
              Text('Шрифт', style: Theme.of(ctx).textTheme.titleSmall?.copyWith(fontWeight: FontWeight.w800)),
              RadioListTile<String>(
                title: const Text('Системный'),
                value: 'system',
                groupValue: _fontKey,
                onChanged: (v) async {
                  if (v == null) {
                    return;
                  }
                  setState(() => _fontKey = v);
                  await ReaderPrefs.saveFontKey(v);
                  if (!mounted) {
                    return;
                  }
                  _invalidatePaginationLayout();
                  if (ctx.mounted) {
                    Navigator.pop(ctx);
                  }
                },
              ),
              RadioListTile<String>(
                title: const Text('С засечками (serif)'),
                value: 'serif',
                groupValue: _fontKey,
                onChanged: (v) async {
                  if (v == null) {
                    return;
                  }
                  setState(() => _fontKey = v);
                  await ReaderPrefs.saveFontKey(v);
                  if (!mounted) {
                    return;
                  }
                  _invalidatePaginationLayout();
                  if (ctx.mounted) {
                    Navigator.pop(ctx);
                  }
                },
              ),
              RadioListTile<String>(
                title: const Text('Моноширинный'),
                value: 'mono',
                groupValue: _fontKey,
                onChanged: (v) async {
                  if (v == null) {
                    return;
                  }
                  setState(() => _fontKey = v);
                  await ReaderPrefs.saveFontKey(v);
                  if (!mounted) {
                    return;
                  }
                  _invalidatePaginationLayout();
                  if (ctx.mounted) {
                    Navigator.pop(ctx);
                  }
                },
              ),
              const SizedBox(height: 8),
              Row(
                children: <Widget>[
                  Text('Размер: ${_sheetFontSize.round()} pt'),
                  Expanded(
                    child: Slider(
                      value: _sheetFontSize.clamp(12, 32),
                      min: 12,
                      max: 32,
                      divisions: 20,
                      label: _sheetFontSize.round().toString(),
                      onChanged: (v) {
                        setState(() => _sheetFontSize = v);
                        _fontSizeDebounce?.cancel();
                        _fontSizeDebounce = Timer(const Duration(milliseconds: 500), () {
                          if (!mounted) {
                            return;
                          }
                          setState(() => _fontSize = _sheetFontSize);
                          _invalidatePaginationLayout();
                        });
                      },
                      onChangeEnd: (v) {
                        _fontSizeDebounce?.cancel();
                        final alreadyApplied = v == _fontSize;
                        setState(() {
                          _fontSize = v;
                          _sheetFontSize = v;
                        });
                        ReaderPrefs.saveFontSize(v);
                        if (!alreadyApplied) {
                          _invalidatePaginationLayout();
                        }
                      },
                    ),
                  ),
                ],
              ),
            ],
          ),
        );
      },
    ).whenComplete(() => _fontSizeDebounce?.cancel());
  }

  /// Отступы как у страницы: `Padding(..., 16, 8, 16, 8)`.
  static const double _pagePadH = 16 * 2;
  static const double _pagePadV = 8 * 2;

  double _textScaleLinear(TextScaler ts, double fontSize) {
    if (fontSize <= 0) {
      return 1;
    }
    return ts.scale(fontSize) / fontSize;
  }

  /// Не вызывать setState из build: только постановка post-frame.
  ///
  /// [constraints] — только область [PageView] (дочерний [Expanded]), без полосы прогресса.
  void _schedulePaginationIfNeeded(BoxConstraints constraints, TextStyle bodyStyle) {
    final media = MediaQuery.of(context);
    final ts = MediaQuery.textScalerOf(context);

    double maxH = constraints.maxHeight;
    if (!maxH.isFinite || maxH <= 0) {
      maxH = media.size.height;
    }
    double maxW = constraints.maxWidth;
    if (!maxW.isFinite || maxW <= 0) {
      maxW = media.size.width;
    }

    var h = maxH - _pagePadV;
    var w = maxW - _pagePadH;
    if (!w.isFinite || !h.isFinite) {
      return;
    }
    const minReadable = 16.0;
    if (h < minReadable) {
      h = math.max(minReadable, media.size.height - _pagePadV - 160);
    }
    if (w < minReadable) {
      w = math.max(minReadable, media.size.width - _pagePadH);
    }
    final fs = bodyStyle.fontSize ?? 17;
    final scaleLin = _textScaleLinear(ts, fs);
    final sig =
        '${w.toInt()}_${h.toInt()}_${bodyStyle.fontSize}_${bodyStyle.fontFamily}_${widget.fullText.length}_${scaleLin.toStringAsFixed(3)}';

    if (sig == _layoutSig && _paginationReady) {
      return;
    }
    if (_paginationScheduledForSig == sig) {
      return;
    }
    _paginationScheduledForSig = sig;

    WidgetsBinding.instance.addPostFrameCallback((_) {
      _paginationScheduledForSig = null;
      if (!mounted) {
        return;
      }
      _paginateSeq++;
      final capturedSeq = _paginateSeq;
      unawaited(_runPaginationAfterLayout(capturedSeq, constraints, bodyStyle, sig));
    });
  }

  Future<void> _runPaginationAfterLayout(
    int capturedSeq,
    BoxConstraints constraints,
    TextStyle bodyStyle,
    String sig,
  ) async {
    if (!mounted || capturedSeq != _paginateSeq) {
      return;
    }

    if (widget.fullText.isEmpty) {
      if (!mounted || capturedSeq != _paginateSeq) {
        return;
      }
      setState(() {
        _layoutSig = sig;
        _pages = <String>[''];
        _current = 0;
        _paginationReady = true;
      });
      return;
    }

    final media = MediaQuery.of(context);
    final ts = MediaQuery.textScalerOf(context);

    double maxH = constraints.maxHeight;
    if (!maxH.isFinite || maxH <= 0) {
      maxH = media.size.height;
    }
    double maxW = constraints.maxWidth;
    if (!maxW.isFinite || maxW <= 0) {
      maxW = media.size.width;
    }

    var h = maxH - _pagePadV;
    var w = maxW - _pagePadH;
    const minReadable = 16.0;
    if (h < minReadable) {
      h = math.max(minReadable, media.size.height - _pagePadV - 160);
    }
    if (w < minReadable) {
      w = math.max(minReadable, media.size.width - _pagePadH);
    }
    final fs = bodyStyle.fontSize ?? 17;
    final scaleLin = _textScaleLinear(ts, fs);
    final freshSig =
        '${w.toInt()}_${h.toInt()}_${bodyStyle.fontSize}_${bodyStyle.fontFamily}_${widget.fullText.length}_${scaleLin.toStringAsFixed(3)}';

    if (!mounted || capturedSeq != _paginateSeq) {
      return;
    }
    if (freshSig == _layoutSig && _paginationReady) {
      return;
    }

    List<String> split;
    try {
      final asyncResult = await paginateTextForHeightAsync(
        text: widget.fullText,
        maxWidth: w,
        maxHeight: h,
        style: bodyStyle,
        textScaler: ts,
        shouldAbort: () => !mounted || capturedSeq != _paginateSeq,
      );
      if (!mounted || capturedSeq != _paginateSeq) {
        return;
      }
      if (asyncResult == null) {
        return;
      }
      split = asyncResult;
    } catch (e, st) {
      debugPrint('paginateTextForHeightAsync: $e\n$st');
      split = <String>[widget.fullText];
    }

    if (!mounted || capturedSeq != _paginateSeq) {
      return;
    }

    setState(() {
      _layoutSig = freshSig;
      _pages = split.isEmpty ? <String>[''] : split;
      if (_current >= _pages.length) {
        _current = _pages.length - 1;
      }
      _paginationReady = true;
    });
    _maybeHydrateProgress();
  }

  bool _hydrating = false;

  Future<void> _maybeHydrateProgress() async {
    if (_hydrating || widget.bookId == null || _pages.isEmpty || _didJumpInitial) {
      return;
    }
    _hydrating = true;
    try {
      setState(() => _syncLoading = true);
      final repo = context.read<BooksRepository>();
      final prog = await repo.getReadingProgress(widget.bookId!);
      final marks = await repo.listBookmarks(widget.bookId!);
      if (!mounted) {
        return;
      }
      setState(() {
        _bookmarks = marks;
        _syncLoading = false;
      });
      final idx = resolveInitialPageIndex(
        progress: prog,
        totalPages: _pages.length,
      );
      if (idx != null) {
        final safe = idx.clamp(0, _pages.length - 1);
        _didJumpInitial = true;
        _scheduleJumpTo(safe);
      } else {
        _didJumpInitial = true;
      }
    } catch (_) {
      if (mounted) {
        setState(() => _syncLoading = false);
        _didJumpInitial = true;
      }
    } finally {
      _hydrating = false;
    }
  }

  void _scheduleJumpTo(int safeIdx) {
    void jump() {
      if (!mounted) {
        return;
      }
      if (!_pageController.hasClients) {
        WidgetsBinding.instance.addPostFrameCallback((_) => jump());
        return;
      }
      setState(() => _current = safeIdx);
      _pageController.jumpToPage(safeIdx);
    }

    jump();
  }

  void _onPageChanged(int i) {
    setState(() => _current = i);
    _debouncedSave();
  }

  void _debouncedSave() {
    if (widget.bookId == null) {
      return;
    }
    _saveDebounce?.cancel();
    _saveDebounce = Timer(const Duration(milliseconds: 700), _flushProgress);
  }

  Future<void> _flushProgress() async {
    if (!mounted || widget.bookId == null || _pages.isEmpty) {
      return;
    }
    final total = _pages.length;
    final pos = jsonEncode(<String, dynamic>{
      'page': _current,
      'totalPages': total,
      'reader': 'mobile_text',
    });
    final pct = total <= 1 ? 100 : (((_current + 1) * 100) / total).round().clamp(0, 100);
    try {
      await context.read<BooksRepository>().saveReadingProgress(
            bookId: widget.bookId!,
            page: _current + 1,
            position: pos,
            percent: pct,
          );
    } catch (_) {}
  }

  Future<void> _addBookmark() async {
    if (widget.bookId == null) {
      return;
    }
    final titleCtrl = TextEditingController(text: 'Стр. ${_current + 1}');
    final noteCtrl = TextEditingController();
    final ok = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Закладка и заметка'),
        content: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: <Widget>[
              TextField(
                controller: titleCtrl,
                decoration: const InputDecoration(labelText: 'Заголовок'),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: noteCtrl,
                decoration: const InputDecoration(labelText: 'Заметка'),
                minLines: 2,
                maxLines: 6,
              ),
            ],
          ),
        ),
        actions: <Widget>[
          TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('Отмена')),
          FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('Сохранить')),
        ],
      ),
    );
    if (ok != true || !mounted) {
      return;
    }
    final repo = context.read<BooksRepository>();
    try {
      final pos = jsonEncode(<String, dynamic>{
        'page': _current,
        'totalPages': _pages.length,
      });
      await repo.addBookmark(
            bookId: widget.bookId!,
            pageLabel: '${_current + 1}',
            position: pos,
            title: titleCtrl.text.trim(),
            note: noteCtrl.text.trim(),
          );
      final marks = await repo.listBookmarks(widget.bookId!);
      if (mounted) {
        setState(() => _bookmarks = marks);
        ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Закладка сохранена')));
      }
    } on ApiException catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
      }
    }
  }

  Future<void> _showBookmarks() async {
    final colors = _themeColors();
    await showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (ctx) {
        return DraggableScrollableSheet(
          expand: false,
          initialChildSize: 0.55,
          maxChildSize: 0.92,
          minChildSize: 0.35,
          builder: (_, scroll) {
            if (_bookmarks.isEmpty) {
              return Center(
                child: Padding(
                  padding: const EdgeInsets.all(24),
                  child: Text('Нет закладок', style: TextStyle(color: colors.fg)),
                ),
              );
            }
            return ListView.builder(
              controller: scroll,
              itemCount: _bookmarks.length,
              itemBuilder: (c, i) {
                final b = _bookmarks[i];
                return ListTile(
                  leading: Icon(Icons.bookmark, color: AppThemeColors.primaryMedium),
                  title: Text(b.title ?? 'Закладка', maxLines: 2, overflow: TextOverflow.ellipsis),
                  subtitle: Text(
                    (b.note ?? '').isEmpty ? (b.page != null ? 'Стр. ${b.page}' : '') : b.note!,
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                  ),
                  onTap: () {
                    Navigator.pop(ctx);
                    _goToBookmark(b);
                  },
                  trailing: IconButton(
                    icon: const Icon(Icons.delete_outline),
                    onPressed: () async {
                      try {
                        await context.read<BooksRepository>().deleteBookmark(b.bookmarkId);
                        if (!mounted) {
                          return;
                        }
                        setState(() {
                          _bookmarks.removeWhere((x) => x.bookmarkId == b.bookmarkId);
                        });
                      } on ApiException catch (e) {
                        if (mounted) {
                          ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
                        }
                      }
                    },
                  ),
                );
              },
            );
          },
        );
      },
    );
  }

  void _goToBookmark(BookmarkApi b) {
    int? pageIdx;
    final raw = b.position?.trim();
    if (raw != null && raw.isNotEmpty) {
      try {
        final m = jsonDecode(raw);
        if (m is Map<String, dynamic>) {
          final p = m['page'] ?? m['mobilePage'];
          if (p is int) {
            pageIdx = p;
          } else if (p is num) {
            pageIdx = p.toInt();
          }
        }
      } catch (_) {}
    }
    if (pageIdx == null && b.page != null && b.page!.isNotEmpty) {
      final n = int.tryParse(b.page!);
      if (n != null) {
        pageIdx = n - 1;
      }
    }
    if (pageIdx != null && pageIdx >= 0 && pageIdx < _pages.length) {
      final safe = pageIdx.clamp(0, _pages.length - 1);
      _scheduleJumpTo(safe);
      _debouncedSave();
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = _themeColors();
    final bodyStyle = _bodyStyle(colors.fg);

    if (!_prefsReady) {
      return Scaffold(
        backgroundColor: colors.bg,
        appBar: AppBar(
          title: Text(widget.title, maxLines: 1, overflow: TextOverflow.ellipsis),
          backgroundColor: colors.bg,
          foregroundColor: colors.appBarFg,
        ),
        body: const Center(child: CircularProgressIndicator()),
      );
    }

    return Scaffold(
      backgroundColor: colors.bg,
      appBar: AppBar(
        backgroundColor: colors.bg,
        foregroundColor: colors.appBarFg,
        title: Text(widget.title, maxLines: 1, overflow: TextOverflow.ellipsis),
        actions: <Widget>[
          if (_syncLoading && widget.bookId != null)
            const Padding(
              padding: EdgeInsets.only(right: 12),
              child: Center(
                child: SizedBox.square(
                  dimension: 18,
                  child: CircularProgressIndicator(strokeWidth: 2),
                ),
              ),
            ),
          IconButton(
            tooltip: 'Тема',
            onPressed: _cycleTheme,
            icon: Icon(_theme == ReaderThemeMode.dark ? Icons.dark_mode : Icons.light_mode_outlined),
          ),
          IconButton(
            tooltip: 'Шрифт и размер',
            onPressed: _showAppearanceSheet,
            icon: const Icon(Icons.text_fields),
          ),
          if (widget.bookId != null) ...<Widget>[
            IconButton(
              tooltip: 'Закладки',
              onPressed: _showBookmarks,
              icon: const Icon(Icons.bookmarks_outlined),
            ),
            IconButton(
              tooltip: 'Добавить закладку',
              onPressed: _addBookmark,
              icon: const Icon(Icons.bookmark_add_outlined),
            ),
          ],
        ],
      ),
      body: Column(
        children: <Widget>[
          Expanded(
            child: LayoutBuilder(
              builder: (context, constraints) {
                _schedulePaginationIfNeeded(constraints, bodyStyle);
                final bool showPaging = widget.fullText.isEmpty || _paginationReady;
                if (widget.fullText.isNotEmpty && !showPaging) {
                  return const Center(child: CircularProgressIndicator());
                }
                return PageView.builder(
                  controller: _pageController,
                  itemCount: _pages.length,
                  onPageChanged: _onPageChanged,
                  itemBuilder: (context, i) {
                    return Padding(
                      padding: const EdgeInsets.fromLTRB(16, 8, 16, 8),
                      child: Align(
                        alignment: Alignment.topCenter,
                        child: SelectableText(
                          _pages[i],
                          style: bodyStyle,
                        ),
                      ),
                    );
                  },
                );
              },
            ),
          ),
          LayoutBuilder(
            builder: (context, _) {
              final bool showPaging = widget.fullText.isEmpty || _paginationReady;
              final progress = !showPaging || _pages.isEmpty ? 0.0 : (_current + 1) / _pages.length;
              return Container(
                padding: EdgeInsets.fromLTRB(12, 6, 12, 4 + MediaQuery.of(context).padding.bottom),
                color: colors.bg.withValues(alpha: 0.95),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: <Widget>[
                    ClipRRect(
                      borderRadius: BorderRadius.circular(6),
                      child: LinearProgressIndicator(
                        value: !showPaging ? null : progress.clamp(0.0, 1.0),
                        minHeight: 5,
                        backgroundColor: colors.fg.withValues(alpha: 0.12),
                        color: AppThemeColors.primaryMedium,
                      ),
                    ),
                    const SizedBox(height: 6),
                    Row(
                      mainAxisAlignment: MainAxisAlignment.spaceBetween,
                      children: <Widget>[
                        Text(
                          showPaging ? 'Стр. ${_current + 1} / ${_pages.length}' : 'Разметка страниц…',
                          style: TextStyle(
                            color: colors.fg.withValues(alpha: 0.85),
                            fontWeight: FontWeight.w600,
                            fontSize: 13,
                          ),
                        ),
                        Text(
                          showPaging ? '${(progress * 100).round()}%' : '…',
                          style: TextStyle(color: colors.fg.withValues(alpha: 0.7), fontSize: 13),
                        ),
                      ],
                    ),
                  ],
                ),
              );
            },
          ),
        ],
      ),
    );
  }
}
