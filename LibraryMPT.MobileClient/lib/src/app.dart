import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import 'core/network/api_client.dart';
import 'core/theme/app_theme.dart';
import 'features/auth/data/auth_repository.dart';
import 'features/auth/presentation/login_screen.dart';
import 'features/books/data/books_repository.dart';
import 'features/cabinet/data/cabinet_repository.dart';
import 'features/shell/presentation/client_shell.dart';

class LibraryMptMobileApp extends StatelessWidget {
  const LibraryMptMobileApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MultiProvider(
      providers: [
        Provider<ApiClient>(create: (_) => ApiClient()),
        ProxyProvider<ApiClient, AuthRepository>(
          update: (_, api, __) => AuthRepository(api),
        ),
        ProxyProvider<ApiClient, BooksRepository>(
          update: (_, api, __) => BooksRepository(api),
        ),
        ProxyProvider<ApiClient, CabinetRepository>(
          update: (_, api, __) => CabinetRepository(api),
        ),
      ],
      child: MaterialApp(
        title: 'Электронная библиотека',
        debugShowCheckedModeBanner: false,
        theme: AppTheme.light,
        home: const _BootstrapScreen(),
      ),
    );
  }
}

class _BootstrapScreen extends StatefulWidget {
  const _BootstrapScreen();

  @override
  State<_BootstrapScreen> createState() => _BootstrapScreenState();
}

class _BootstrapScreenState extends State<_BootstrapScreen> {
  bool _loading = true;
  bool _hasToken = false;

  @override
  void initState() {
    super.initState();
    _restoreToken();
  }

  Future<void> _restoreToken() async {
    final authRepository = context.read<AuthRepository>();
    final token = await authRepository.restoreToken();
    if (!mounted) {
      return;
    }
    setState(() {
      _hasToken = token != null && token.isNotEmpty;
      _loading = false;
    });
  }

  @override
  Widget build(BuildContext context) {
    if (_loading) {
      return const Scaffold(
        body: Center(child: CircularProgressIndicator()),
      );
    }

    if (_hasToken) {
      return ClientShell(
        onSignOut: () async {
          await context.read<AuthRepository>().signOut();
          if (!mounted) {
            return;
          }
          setState(() {
            _hasToken = false;
          });
        },
      );
    }

    return LoginScreen(
      onLoggedIn: () {
        setState(() {
          _hasToken = true;
        });
      },
    );
  }
}
