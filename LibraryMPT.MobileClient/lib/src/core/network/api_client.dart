import 'dart:convert';

import 'package:http/http.dart' as http;

import '../config/app_config.dart';
import 'api_exception.dart';

class BinaryApiResponse {
  BinaryApiResponse({
    required this.bodyBytes,
    this.suggestedFileName,
  });

  final List<int> bodyBytes;
  final String? suggestedFileName;
}

class ApiClient {
  ApiClient({http.Client? httpClient}) : _httpClient = httpClient ?? http.Client();

  final http.Client _httpClient;
  String? _accessToken;
  String? get accessToken => _accessToken;

  void setAccessToken(String? token) {
    _accessToken = token;
  }

  Future<Map<String, dynamic>> getJson(
    String path, {
    Map<String, String>? queryParameters,
    Map<String, List<String>>? queryParametersAll,
    bool authorized = false,
  }) async {
    final uri = _buildUri(path, queryParameters, queryParametersAll: queryParametersAll);
    final response = await _httpClient.get(uri, headers: _buildHeaders(authorized));
    return _parseObject(response);
  }

  Future<List<dynamic>> getJsonList(
    String path, {
    Map<String, String>? queryParameters,
    bool authorized = false,
  }) async {
    final uri = _buildUri(path, queryParameters);
    final response = await _httpClient.get(uri, headers: _buildHeaders(authorized));
    return _parseList(response);
  }

  Future<BinaryApiResponse> getBytes(
    String path, {
    bool authorized = false,
    Map<String, String>? queryParameters,
    Map<String, List<String>>? queryParametersAll,
  }) async {
    final uri = _buildUri(path, queryParameters, queryParametersAll: queryParametersAll);
    final response = await _httpClient.get(
      uri,
      headers: _buildHeadersBinary(authorized),
    );
    if (response.statusCode < 200 || response.statusCode >= 300) {
      var message = 'HTTP ${response.statusCode}: ${response.reasonPhrase ?? ''}'.trim();
      if (response.body.isNotEmpty) {
        try {
          final decoded = jsonDecode(response.body);
          if (decoded is Map<String, dynamic>) {
            final m = decoded['message'] ?? decoded['Message'];
            if (m != null && m.toString().isNotEmpty) {
              message = m.toString();
            }
          }
        } catch (_) {
          if (response.body.length < 500) {
            message = '${response.statusCode}: ${response.body}';
          }
        }
      }
      throw ApiException(message, statusCode: response.statusCode);
    }
    final fileName = _parseContentDispositionFileName(response.headers['content-disposition']);
    return BinaryApiResponse(
      bodyBytes: response.bodyBytes,
      suggestedFileName: fileName,
    );
  }

  Future<Map<String, dynamic>> postJson(
    String path, {
    Object? body,
    bool authorized = false,
  }) async {
    final uri = _buildUri(path, null);
    final response = await _httpClient.post(
      uri,
      headers: _buildHeaders(authorized),
      body: body == null ? null : jsonEncode(body),
    );
    return _parseObject(response);
  }

  Future<Map<String, dynamic>?> getJsonOrNull(
    String path, {
    Map<String, String>? queryParameters,
    bool authorized = false,
  }) async {
    final uri = _buildUri(path, queryParameters);
    final response = await _httpClient.get(uri, headers: _buildHeaders(authorized));
    if (response.statusCode < 200 || response.statusCode >= 300) {
      if (response.statusCode == 404) {
        return null;
      }
      throw ApiException(
        'HTTP ${response.statusCode}: ${response.body}',
        statusCode: response.statusCode,
      );
    }
    final b = response.body.trim();
    if (b.isEmpty || b == 'null') {
      return null;
    }
    final decoded = jsonDecode(response.body);
    if (decoded == null) {
      return null;
    }
    if (decoded is Map<String, dynamic>) {
      return decoded;
    }
    return null;
  }

  Future<Map<String, dynamic>> deleteJson(String path, {bool authorized = false}) async {
    final uri = _buildUri(path, null);
    final response = await _httpClient.delete(uri, headers: _buildHeaders(authorized));
    return _parseObject(response);
  }

  Uri _buildUri(
    String path,
    Map<String, String>? queryParameters, {
    Map<String, List<String>>? queryParametersAll,
  }) {
    final base = Uri.parse(AppConfig.apiBaseUrl);
    final normalizedPath = path.startsWith('/') ? path : '/$path';
    final mergedPath = '${base.path}$normalizedPath'.replaceAll('//', '/');

    Map<String, List<String>>? all;
    if (queryParameters != null && queryParameters.isNotEmpty) {
      all = <String, List<String>>{
        for (final entry in queryParameters.entries) entry.key: <String>[entry.value],
      };
    }
    if (queryParametersAll != null) {
      all ??= <String, List<String>>{};
      for (final entry in queryParametersAll.entries) {
        all[entry.key] = List<String>.from(entry.value);
      }
    }

    if (all == null) {
      return base.replace(path: mergedPath);
    }
    final rawQueryParts = <String>[];
    for (final entry in all.entries) {
      for (final value in entry.value) {
        rawQueryParts.add('${Uri.encodeQueryComponent(entry.key)}=${Uri.encodeQueryComponent(value)}');
      }
    }
    final query = rawQueryParts.join('&');
    return base.replace(path: mergedPath, query: query.isEmpty ? null : query);
  }

  Map<String, String> _buildHeaders(bool authorized) {
    final headers = <String, String>{
      'Content-Type': 'application/json',
      'Accept': 'application/json',
    };

    if (authorized) {
      final token = _accessToken;
      if (token == null || token.isEmpty) {
        throw ApiException('Отсутствует access token для авторизованного запроса.');
      }
      headers['Authorization'] = 'Bearer $token';
    }

    return headers;
  }

  Map<String, String> _buildHeadersBinary(bool authorized) {
    final headers = <String, String>{
      'Accept': '*/*',
    };

    if (authorized) {
      final token = _accessToken;
      if (token == null || token.isEmpty) {
        throw ApiException('Отсутствует access token для авторизованного запроса.');
      }
      headers['Authorization'] = 'Bearer $token';
    }

    return headers;
  }

  String? _parseContentDispositionFileName(String? raw) {
    if (raw == null || raw.isEmpty) {
      return null;
    }
    final lowered = raw.toLowerCase();
    final starIdx = lowered.indexOf('filename*=');
    if (starIdx >= 0) {
      var token = raw.substring(starIdx + 'filename*='.length).trim();
      final semi = token.indexOf(';');
      if (semi >= 0) {
        token = token.substring(0, semi).trim();
      }
      final utfMarker = "utf-8''";
      if (token.toLowerCase().startsWith(utfMarker)) {
        token = token.substring(utfMarker.length);
        try {
          return Uri.decodeComponent(token).trim();
        } catch (_) {
          return token.trim();
        }
      }
    }
    final idx = lowered.indexOf('filename=');
    if (idx < 0) {
      return null;
    }
    var name = raw.substring(idx + 'filename='.length).trim();
    final semi = name.indexOf(';');
    if (semi >= 0) {
      name = name.substring(0, semi).trim();
    }
    if (name.startsWith('"') && name.endsWith('"') && name.length >= 2) {
      name = name.substring(1, name.length - 1);
    }
    return name.isEmpty ? null : name;
  }

  Map<String, dynamic> _parseObject(http.Response response) {
    final jsonValue = _parseJson(response);
    if (jsonValue is Map<String, dynamic>) {
      return jsonValue;
    }
    throw ApiException('Ожидался JSON-объект.', statusCode: response.statusCode);
  }

  List<dynamic> _parseList(http.Response response) {
    final jsonValue = _parseJson(response);
    if (jsonValue is List<dynamic>) {
      return jsonValue;
    }
    throw ApiException('Ожидался JSON-массив.', statusCode: response.statusCode);
  }

  dynamic _parseJson(http.Response response) {
    if (response.statusCode < 200 || response.statusCode >= 300) {
      throw ApiException(
        'HTTP ${response.statusCode}: ${response.body}',
        statusCode: response.statusCode,
      );
    }

    if (response.body.isEmpty) {
      return <String, dynamic>{};
    }

    return jsonDecode(response.body);
  }
}
