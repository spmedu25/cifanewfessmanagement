import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/material.dart';
import 'package:flutter_local_notifications/flutter_local_notifications.dart';
import 'api.dart';
import 'screens/chat.dart';

final GlobalKey<NavigatorState> navKey = GlobalKey<NavigatorState>();
/*
@pragma('vm:entry-point')
Future<void> _bgHandler(RemoteMessage m) async {
  // 1. Firebase initialize karein background isolate ke liye
  await Firebase.initializeApp();

  final n = m.notification;
  final data = m.data;

  // 2. Title aur body extract karein
  final title = n?.title ?? data['title'] ?? 'Cifa Coaching';
  final body = n?.body ?? data['body'] ?? 'Aapko ek naya message mila hai';

  // 3. Local notifications plugin ko background mein initialize karein
  final local = FlutterLocalNotificationsPlugin();

  // Zaroori: Background isolate ke liye settings initialize karna padta hai
  const androidSettings = AndroidInitializationSettings('@mipmap/ic_launcher');
  const initSettings = InitializationSettings(android: androidSettings, iOS: DarwinInitializationSettings());
  await local.initialize(initSettings);

  const androidDetails = AndroidNotificationDetails(
    'coaching_chat',
    'Messages & alerts',
    channelDescription: 'Chat messages, fees and admission alerts',
    importance: Importance.high,
    priority: Priority.high,
  );

  const notificationDetails = NotificationDetails(
    android: androidDetails,
    iOS: DarwinNotificationDetails(),
  );

  await local.show(
    m.hashCode,
    title,
    body,
    notificationDetails,
    payload: jsonEncode(data),
  );
}
 */
@pragma('vm:entry-point')
Future<void> _bgHandler(RemoteMessage m) async {
  // 1. Firebase initialize karein background isolate ke liye
  await Firebase.initializeApp();

  final n = m.notification;
  final data = m.data;

  // 2. Title aur body extract karein
  final title = n?.title ?? data['title'] ?? 'Cifa Coaching';
  final body = n?.body ?? data['body'] ?? 'Aapko ek naya message mila hai';

  // 3. Local notifications plugin ke through background mein notification dikhayein
  final local = FlutterLocalNotificationsPlugin();

  const androidDetails = AndroidNotificationDetails(
    'coaching_chat',
    'Messages & alerts',
    channelDescription: 'Chat messages, fees and admission alerts',
    importance: Importance.high,
    priority: Priority.high,
  );

  const notificationDetails = NotificationDetails(
    android: androidDetails,
    iOS: DarwinNotificationDetails(),
  );

  await local.show(
    m.hashCode,
    title,
    body,
    notificationDetails,
    payload: jsonEncode(data),
  );
}

class Push {
  static final _local = FlutterLocalNotificationsPlugin();
  static bool ready = false;
  static String? activeChat;                       // abhi khula chat ('student' ya 'admin:ID') - uska notification nahi dikhate
  static final bus = StreamController<Map<String, dynamic>>.broadcast();   // naya push aaya -> screens refresh
  static const _channel = AndroidNotificationChannel('coaching_chat', 'Messages & alerts',
      description: 'Chat messages, fees and admission alerts', importance: Importance.high);

  static Future<void> init() async {
    try {
      await Firebase.initializeApp();
      FirebaseMessaging.onBackgroundMessage(_bgHandler);
      await _local.initialize(
        const InitializationSettings(android: AndroidInitializationSettings('@mipmap/ic_launcher'), iOS: DarwinInitializationSettings()),
        onDidReceiveNotificationResponse: (r) {
          if (r.payload != null) _open(Map<String, dynamic>.from(jsonDecode(r.payload!)));
        },
      );
      await _local.resolvePlatformSpecificImplementation<AndroidFlutterLocalNotificationsPlugin>()?.createNotificationChannel(_channel);
      await FirebaseMessaging.instance.requestPermission();
      FirebaseMessaging.onMessage.listen(_foreground);
      FirebaseMessaging.onMessageOpenedApp.listen((m) => _open(m.data));
      FirebaseMessaging.instance.onTokenRefresh.listen(_send);
      final initial = await FirebaseMessaging.instance.getInitialMessage();
      if (initial != null) WidgetsBinding.instance.addPostFrameCallback((_) => Future.delayed(const Duration(milliseconds: 600), () => _open(initial.data)));
      ready = true;
    } catch (e) {
      debugPrint('Push disabled (Firebase configure nahi hai?): $e');
    }
  }

  static Future<void> _foreground(RemoteMessage m) async {
    final data = Map<String, dynamic>.from(m.data);
    bus.add(data);
    final key = Api.role == 'Admin' ? 'admin:${data['studentId']}' : 'student';
    if (data['type'] == 'chat' && activeChat == key) return;      // user isi chat me hai
    final n = m.notification;
    await _local.show(
      m.hashCode, n?.title, n?.body,
      NotificationDetails(
        android: AndroidNotificationDetails(_channel.id, _channel.name,
            channelDescription: _channel.description, importance: Importance.high, priority: Priority.high),
        iOS: const DarwinNotificationDetails(),
      ),
      payload: jsonEncode(data),
    );
  }

  static void _open(Map<String, dynamic> data) {
    if (Api.token == null) return;
    final nav = navKey.currentState;
    if (nav == null || data['type'] != 'chat') return;
    if (Api.role == 'Admin') {
      final id = int.tryParse('${data['studentId']}');
      if (id != null) nav.push(MaterialPageRoute(builder: (_) => AdminChatScreen(studentId: id)));
    } else {
      nav.push(MaterialPageRoute(builder: (_) => const StudentChatScreen()));
    }
  }

  static Future<void> register() async {
    if (!ready || Api.token == null) return;
    try {
      final t = await FirebaseMessaging.instance.getToken();
      if (t != null) await _send(t);
    } catch (_) {}
  }

  static Future<void> _send(String t) async {
    if (Api.token == null) return;
    try { await Api.post('/api/device/register', {'token': t, 'platform': Platform.operatingSystem}); } catch (_) {}
  }

  static Future<void> unregister() async {
    if (!ready) return;
    try {
      final t = await FirebaseMessaging.instance.getToken();
      if (t != null) await Api.post('/api/device/unregister', {'token': t});
    } catch (_) {}
  }
}
