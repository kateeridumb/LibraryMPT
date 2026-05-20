import 'package:flutter/material.dart';

import '../../../core/theme/app_theme.dart';
import '../../books/presentation/books_screen.dart';
import '../../cabinet/presentation/cabinet_screen.dart';

class ClientShell extends StatefulWidget {
  const ClientShell({required this.onSignOut, super.key});

  final Future<void> Function() onSignOut;

  @override
  State<ClientShell> createState() => _ClientShellState();
}

class _ClientShellState extends State<ClientShell> {
  int _currentIndex = 0;

  @override
  Widget build(BuildContext context) {
    final screens = <Widget>[
      const BooksScreen(),
      const CabinetScreen(),
    ];

    return Scaffold(
      body: IndexedStack(
        index: _currentIndex,
        children: screens,
      ),
      bottomNavigationBar: NavigationBar(
        backgroundColor: Colors.white,
        surfaceTintColor: Colors.transparent,
        selectedIndex: _currentIndex,
        onDestinationSelected: (index) {
          setState(() {
            _currentIndex = index;
          });
        },
        destinations: const <NavigationDestination>[
          NavigationDestination(
            icon: Icon(Icons.menu_book_outlined),
            label: 'Книги',
          ),
          NavigationDestination(
            icon: Icon(Icons.person_outline),
            label: 'Кабинет',
          ),
        ],
      ),
      floatingActionButton: FloatingActionButton.extended(
        backgroundColor: AppThemeColors.primaryDark,
        foregroundColor: Colors.white,
        onPressed: () async {
          await widget.onSignOut();
        },
        icon: const Icon(Icons.logout),
        label: const Text('Выход'),
      ),
    );
  }
}
