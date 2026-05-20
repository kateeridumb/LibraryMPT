import 'package:shared_preferences/shared_preferences.dart';

enum ReaderThemeMode { light, sepia, dark }

/// Локальные настройки оформления читалки.
class ReaderPrefs {
  ReaderPrefs._();

  static const _kTheme = 'reader_theme_mode';
  static const _kFont = 'reader_font_family';
  static const _kSize = 'reader_font_size';

  static Future<ReaderThemeMode> loadTheme() async {
    final p = await SharedPreferences.getInstance();
    final v = p.getInt(_kTheme) ?? 0;
    final i = v.clamp(0, ReaderThemeMode.values.length - 1).toInt();
    return ReaderThemeMode.values[i];
  }

  static Future<void> saveTheme(ReaderThemeMode mode) async {
    final p = await SharedPreferences.getInstance();
    await p.setInt(_kTheme, mode.index);
  }

  static Future<String> loadFontKey() async {
    final p = await SharedPreferences.getInstance();
    return p.getString(_kFont) ?? 'system';
  }

  static Future<void> saveFontKey(String key) async {
    final p = await SharedPreferences.getInstance();
    await p.setString(_kFont, key);
  }

  static Future<double> loadFontSize() async {
    final p = await SharedPreferences.getInstance();
    return p.getDouble(_kSize) ?? 17.0;
  }

  static Future<void> saveFontSize(double size) async {
    final p = await SharedPreferences.getInstance();
    await p.setDouble(_kSize, size.clamp(12.0, 32.0));
  }
}
