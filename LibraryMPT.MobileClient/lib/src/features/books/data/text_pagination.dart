import 'dart:math' as math;

import 'package:flutter/material.dart';

/// Делит текст на страницы по высоте области чтения (как при вёрстке книги).
List<String> paginateTextForHeight({
  required String text,
  required double maxWidth,
  required double maxHeight,
  required TextStyle style,
  TextDirection direction = TextDirection.ltr,
  TextScaler textScaler = TextScaler.noScaling,
}) {
  if (text.isEmpty) {
    return <String>[''];
  }
  if (maxWidth < 8 || maxHeight < 8) {
    return <String>[text];
  }

  final pages = <String>[];
  var start = 0;
  final len = text.length;
  const maxSegment = 48000;

  while (start < len) {
    final roomEnd = math.min(start + maxSegment, len);
    var end = _findLargestEndFitting(
      text: text,
      start: start,
      endMax: roomEnd,
      maxWidth: maxWidth,
      maxHeight: maxHeight,
      style: style,
      direction: direction,
      textScaler: textScaler,
    );
    if (end <= start) {
      end = math.min(start + 1, len);
    }
    end = _snapBreakToNewline(text, start, end);
    pages.add(text.substring(start, end).trimRight());
    start = end;
    while (start < len && (text[start] == '\n' || text[start] == '\r')) {
      start++;
    }
  }

  if (pages.isEmpty) {
    return <String>[text];
  }
  return pages;
}

/// Постраничная вёрстка с отдачей кадра между страницами (индикатор загрузки не замирает).
///
/// Возвращает `null`, если [shouldAbort] вернул true (новая вёрстка, dispose).
Future<List<String>?> paginateTextForHeightAsync({
  required String text,
  required double maxWidth,
  required double maxHeight,
  required TextStyle style,
  TextDirection direction = TextDirection.ltr,
  TextScaler textScaler = TextScaler.noScaling,
  required bool Function() shouldAbort,
}) async {
  if (text.isEmpty) {
    return <String>[''];
  }
  if (maxWidth < 8 || maxHeight < 8) {
    return <String>[text];
  }

  final pages = <String>[];
  var start = 0;
  final len = text.length;
  const maxSegment = 48000;

  while (start < len) {
    if (shouldAbort()) {
      return null;
    }

    final roomEnd = math.min(start + maxSegment, len);
    var end = _findLargestEndFitting(
      text: text,
      start: start,
      endMax: roomEnd,
      maxWidth: maxWidth,
      maxHeight: maxHeight,
      style: style,
      direction: direction,
      textScaler: textScaler,
    );
    if (end <= start) {
      end = math.min(start + 1, len);
    }
    end = _snapBreakToNewline(text, start, end);
    pages.add(text.substring(start, end).trimRight());
    start = end;
    while (start < len && (text[start] == '\n' || text[start] == '\r')) {
      start++;
    }

    await Future<void>.delayed(Duration.zero);
  }

  if (pages.isEmpty) {
    return <String>[text];
  }
  return pages;
}

int _snapBreakToNewline(String text, int start, int end) {
  if (end >= text.length) {
    return end;
  }
  final slice = text.substring(start, end);
  final p = slice.lastIndexOf('\n\n');
  if (p >= slice.length ~/ 4) {
    return start + p + 2;
  }
  final p2 = slice.lastIndexOf('\n');
  if (p2 >= slice.length ~/ 5) {
    return start + p2 + 1;
  }
  return end;
}

int _findLargestEndFitting({
  required String text,
  required int start,
  required int endMax,
  required double maxWidth,
  required double maxHeight,
  required TextStyle style,
  required TextDirection direction,
  TextScaler textScaler = TextScaler.noScaling,
}) {
  var lo = start + 1;
  var hi = endMax;
  var best = start;
  while (lo <= hi) {
    final mid = (lo + hi) >> 1;
    final sub = text.substring(start, mid);
    final tp = TextPainter(
      text: TextSpan(text: sub, style: style),
      textDirection: direction,
      textScaler: textScaler,
    )..layout(maxWidth: maxWidth);
    if (tp.height <= maxHeight) {
      best = mid;
      lo = mid + 1;
    } else {
      hi = mid - 1;
    }
  }
  return best;
}
