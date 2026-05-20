import 'dart:convert';
import 'dart:io';

import 'package:xml/xml.dart';

/// Декодирование FB2 (обычно UTF-8).
String decodeFb2FileBestEffort(List<int> raw) {
  try {
    return utf8.decode(raw, allowMalformed: false);
  } catch (_) {
    return utf8.decode(raw, allowMalformed: true);
  }
}

/// Парсит FB2 → плоский текст с абзацами и заголовками секций.
///
/// В FictionBook может быть **несколько** `<body>` (основной текст, примечания и т.д.).
/// Брать только первый — часто даёт пустое тело при полном файле.
String fictionBookXmlToPlainText(String xmlString) {
  final doc = XmlDocument.parse(xmlString);
  final bodies = doc.findAllElements('body').toList(growable: false);
  if (bodies.isEmpty) {
    throw const FormatException('В FB2 не найден тег <body>.');
  }
  final out = StringBuffer();

  void visit(XmlElement e) {
    switch (e.name.local) {
      case 'binary':
      case 'image':
        return;
      case 'section':
        for (final c in e.children) {
          if (c is XmlElement) {
            visit(c);
          }
        }
        return;
      case 'title':
        final t = e.innerText.trim();
        if (t.isNotEmpty) {
          out.writeln();
          out.writeln(t);
          out.writeln();
        }
        return;
      case 'subtitle':
      case 'text-author':
      case 'p':
        final t = e.innerText.trim();
        if (t.isNotEmpty) {
          out.writeln(t);
        }
        return;
      case 'empty-line':
        out.writeln();
        return;
      case 'cite':
        final t = e.innerText.trim();
        if (t.isNotEmpty) {
          out.writeln('«$t»');
        }
        return;
      case 'epigraph':
        final t = e.innerText.trim();
        if (t.isNotEmpty) {
          out.writeln('— $t');
        }
        return;
      case 'v':
        final t = e.innerText.trim();
        if (t.isNotEmpty) {
          out.writeln(t);
        }
        return;
      case 'stanza':
      case 'poem':
        for (final c in e.children) {
          if (c is XmlElement) {
            visit(c);
          }
        }
        out.writeln();
        return;
      default:
        for (final c in e.children) {
          if (c is XmlElement) {
            visit(c);
          }
        }
    }
  }

  for (var i = 0; i < bodies.length; i++) {
    if (i > 0) {
      out.writeln();
      out.writeln();
    }
    visit(bodies[i]);
  }

  var plain = out.toString().replaceAll(RegExp(r'\n{3,}'), '\n\n').trim();
  if (plain.isNotEmpty) {
    return plain;
  }

  // Резерв: все <p> по документу (обход пустого / нестандартного body).
  final fromParagraphs = doc
      .findAllElements('p')
      .map((XmlElement e) => e.innerText.trim())
      .where((String s) => s.isNotEmpty)
      .join('\n\n')
      .trim();
  if (fromParagraphs.isNotEmpty) {
    return fromParagraphs;
  }

  return plain;
}

Future<String> loadFb2PlainTextFromFile(File file) async {
  final raw = await file.readAsBytes();
  final s = decodeFb2FileBestEffort(raw);
  return fictionBookXmlToPlainText(s);
}
