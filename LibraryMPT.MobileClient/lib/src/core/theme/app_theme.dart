import 'package:flutter/material.dart';

class AppThemeColors {
  const AppThemeColors._();

  static const Color neutralLight = Color(0xFFF6F7F9);
  static const Color neutralWhite = Color(0xFFFFFFFF);
  static const Color primaryDark = Color(0xFF044857);
  static const Color primaryMedium = Color(0xFF0B6B7A);
  static const Color primaryLight = Color(0xFF4FA3B3);
  static const Color primarySoft = Color(0xFFE6F4F1);
  static const Color accentGold = Color(0xFFF8B400);
  static const Color accentLight = Color(0xFFFFD36A);
  static const Color textDark = Color(0xFF1F2A2E);
  static const Color textLight = Color(0xFF6B7C85);

  static const LinearGradient primaryGradient = LinearGradient(
    begin: Alignment.topLeft,
    end: Alignment.bottomRight,
    colors: <Color>[primaryDark, primaryMedium],
  );

  static const LinearGradient goldGradient = LinearGradient(
    begin: Alignment.topLeft,
    end: Alignment.bottomRight,
    colors: <Color>[accentGold, accentLight],
  );
}

class AppTheme {
  const AppTheme._();

  static ThemeData get light {
    final base = ThemeData(
      useMaterial3: true,
      fontFamily: 'Inter',
      colorScheme: ColorScheme.fromSeed(
        seedColor: AppThemeColors.primaryDark,
        primary: AppThemeColors.primaryDark,
        secondary: AppThemeColors.primaryMedium,
        tertiary: AppThemeColors.accentGold,
        surface: AppThemeColors.neutralWhite,
      ),
    );

    return base.copyWith(
      scaffoldBackgroundColor: AppThemeColors.neutralLight,
      appBarTheme: const AppBarTheme(
        centerTitle: false,
        elevation: 0,
        backgroundColor: Colors.transparent,
        foregroundColor: AppThemeColors.primaryDark,
      ),
      cardTheme: CardThemeData(
        color: AppThemeColors.neutralWhite,
        elevation: 3,
        shadowColor: AppThemeColors.primaryDark.withValues(alpha: 0.12),
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(18),
        ),
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: AppThemeColors.neutralWhite,
        hintStyle: const TextStyle(color: AppThemeColors.textLight),
        contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
        enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(14),
          borderSide: const BorderSide(color: AppThemeColors.primarySoft, width: 2),
        ),
        focusedBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(14),
          borderSide: const BorderSide(color: AppThemeColors.primaryMedium, width: 2),
        ),
      ),
      navigationBarTheme: const NavigationBarThemeData(
        indicatorColor: AppThemeColors.primarySoft,
        labelTextStyle: WidgetStatePropertyAll(
          TextStyle(fontWeight: FontWeight.w600),
        ),
      ),
      elevatedButtonTheme: ElevatedButtonThemeData(
        style: ElevatedButton.styleFrom(
          elevation: 0,
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
          padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 14),
          textStyle: const TextStyle(fontWeight: FontWeight.w700, fontSize: 15),
        ),
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          backgroundColor: AppThemeColors.primaryDark,
          foregroundColor: AppThemeColors.neutralWhite,
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
          padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 14),
          textStyle: const TextStyle(fontWeight: FontWeight.w700, fontSize: 15),
        ),
      ),
      textTheme: base.textTheme.apply(
        bodyColor: AppThemeColors.textDark,
        displayColor: AppThemeColors.primaryDark,
      ),
    );
  }
}
