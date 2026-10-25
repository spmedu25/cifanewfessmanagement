import 'dart:convert';
import 'package:http/http.dart' as http;
import 'package:intl/intl.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'config.dart';

class ApiException implements Exception {
  final String message;
  ApiException(this.message);
  @override
  String toString() => message;
}

String money(num? v) => '₹${NumberFormat('#,##,##0').format(v ?? 0)}';
String fdate(dynamic d) => d == null ? '-' : DateFormat('dd-MM-yyyy').format(DateTime.parse(d.toString()));

class Api {
  static String? token;
  static String? role;
  static String? name;

  static Future<void> load() async {
    final p = await SharedPreferences.getInstance();
    token = p.getString('token');
    role = p.getString('role');
    name = p.getString('name');
  }

  static Future<void> saveSession(Map r) async {
    token = r['token'];
    role = r['role'];
    name = r['name'];
    final p = await SharedPreferences.getInstance();
    await p.setString('token', token!);
    await p.setString('role', role!);
    await p.setString('name', name ?? '');
  }

  static Future<void> logout() async {
    token = null; role = null; name = null;
    final p = await SharedPreferences.getInstance();
    await p.clear();
  }

  static String url(String? path) => path == null ? '' : '$kBaseUrl$path';
  static String printUrl(String path) => '$kBaseUrl$path${path.contains('?') ? '&' : '?'}access_token=$token';

  static Map<String, String> get _h => {
        'Content-Type': 'application/json',
        if (token != null) 'Authorization': 'Bearer $token',
      };

  static dynamic _handle(http.Response r) {
    if (r.statusCode >= 200 && r.statusCode < 300) {
      return r.body.isEmpty ? null : jsonDecode(utf8.decode(r.bodyBytes));
    }
    if (r.statusCode == 401 && token != null) {
      throw ApiException('Session expire ho gaya, dobara login karein');
    }
    var msg = 'Error ${r.statusCode}';
    try {
      final j = jsonDecode(utf8.decode(r.bodyBytes));
      msg = j['error'] ?? j['title'] ?? msg;
    } catch (_) {}
    throw ApiException(msg);
  }

  static Future<dynamic> get(String p) async {
    try {
      return _handle(await http.get(Uri.parse('$kBaseUrl$p'), headers: _h).timeout(const Duration(seconds: 20)));
    } on ApiException {
      rethrow;
    } catch (_) {
      throw ApiException('Server se connect nahi ho pa raha');
    }
  }

  static Future<dynamic> post(String p, [Object? body]) async {
    try {
      return _handle(await http
          .post(Uri.parse('$kBaseUrl$p'), headers: _h, body: jsonEncode(body ?? {}))
          .timeout(const Duration(seconds: 20)));
    } on ApiException {
      rethrow;
    } catch (_) {
      throw ApiException('Server se connect nahi ho pa raha');
    }
  }

  static Future<dynamic> put(String p, Object body) async {
    try {
      return _handle(await http.put(Uri.parse('$kBaseUrl$p'), headers: _h, body: jsonEncode(body)).timeout(const Duration(seconds: 20)));
    } on ApiException {
      rethrow;
    } catch (_) {
      throw ApiException('Server se connect nahi ho pa raha');
    }
  }

  static Future<dynamic> upload(String p, String filePath) async {
    final req = http.MultipartRequest('POST', Uri.parse('$kBaseUrl$p'));
    req.headers['Authorization'] = 'Bearer $token';
    req.files.add(await http.MultipartFile.fromPath('file', filePath));
    return _handle(await http.Response.fromStream(await req.send()));
  }
}
