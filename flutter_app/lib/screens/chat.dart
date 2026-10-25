import 'dart:async';
import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';
import '../api.dart';
import '../push.dart';
import '../ui.dart';
import 'admin_shell.dart' show openUrl;

String _time(String iso) {
  final d = DateTime.parse(iso).toLocal();
  final h = d.hour % 12 == 0 ? 12 : d.hour % 12;
  return '$h:${d.minute.toString().padLeft(2, '0')} ${d.hour >= 12 ? 'PM' : 'AM'}';
}

/// WhatsApp jaisa chat. Har 4 sec poll + push aate hi turant refresh.
class ChatView extends StatefulWidget {
  final String path;        // GET/POST endpoint
  final bool admin;
  final String activeKey;
  final void Function(Map student)? onStudent;
  const ChatView({super.key, required this.path, required this.admin, required this.activeKey, this.onStudent});
  @override
  State<ChatView> createState() => _ChatViewState();
}

class _ChatViewState extends State<ChatView> {
  final List<Map> msgs = [];
  int readUpTo = 0;
  final ctrl = TextEditingController(), scroll = ScrollController();
  Timer? timer;
  StreamSubscription? sub;
  bool sending = false, loaded = false;

  @override
  void initState() {
    super.initState();
    Push.activeChat = widget.activeKey;
    load(initial: true);
    timer = Timer.periodic(const Duration(seconds: 4), (_) => load());
    sub = Push.bus.stream.listen((_) => load());
  }

  @override
  void dispose() {
    timer?.cancel();
    sub?.cancel();
    if (Push.activeChat == widget.activeKey) Push.activeChat = null;
    ctrl.dispose();
    scroll.dispose();
    super.dispose();
  }

  void add(List list) {
    final ids = msgs.map((m) => m['id']).toSet();
    for (final m in list) { if (!ids.contains(m['id'])) msgs.add(Map.from(m)); }
    msgs.sort((a, b) => (a['id'] as int).compareTo(b['id'] as int));
  }

  Future<void> load({bool initial = false}) async {
    try {
      final last = msgs.isEmpty || initial ? 0 : msgs.last['id'];
      final r = await Api.get('${widget.path}?afterId=$last');
      if (!mounted) return;
      final fresh = (r['messages'] as List);
      final newRead = (r['otherReadUpTo'] ?? 0) as int;
      final changed = fresh.isNotEmpty || newRead != readUpTo || !loaded;
      setState(() { add(fresh); readUpTo = newRead; loaded = true; });
      if (r['student'] != null) widget.onStudent?.call(r['student']);
      if (changed && fresh.isNotEmpty) jump();
    } catch (_) {}
  }

  void jump() => WidgetsBinding.instance.addPostFrameCallback((_) {
        if (scroll.hasClients) scroll.animateTo(scroll.position.maxScrollExtent, duration: const Duration(milliseconds: 200), curve: Curves.easeOut);
      });

  Future<void> send() async {
    final t = ctrl.text.trim();
    if (t.isEmpty || sending) return;
    setState(() => sending = true);
    try {
      final m = await Api.post(widget.path, {'text': t});
      ctrl.clear();
      setState(() => add([m]));
      jump();
    } catch (e) { if (mounted) toast(context, e.toString(), error: true); }
    if (mounted) setState(() => sending = false);
  }

  Future<void> sendImage() async {
    final x = await ImagePicker().pickImage(source: ImageSource.gallery, imageQuality: 75, maxWidth: 1280);
    if (x == null) return;
    setState(() => sending = true);
    try {
      final m = await Api.upload('${widget.path}/image', x.path);
      setState(() => add([m]));
      jump();
    } catch (e) { if (mounted) toast(context, e.toString(), error: true); }
    if (mounted) setState(() => sending = false);
  }

  @override
  Widget build(BuildContext context) {
    return Container(
      color: const Color(0xFFEFEAE2),
      child: Column(children: [
        Expanded(
          child: !loaded
              ? const Loader()
              : msgs.isEmpty
                  ? const Center(child: Text('Abhi koi message nahi. Pehla message bhejein 👋'))
                  : ListView.builder(
                      controller: scroll,
                      padding: const EdgeInsets.all(10),
                      itemCount: msgs.length,
                      itemBuilder: (_, i) => _bubble(i),
                    ),
        ),
        SafeArea(
          top: false,
          child: Container(
            color: const Color(0xFFF0F2F5),
            padding: const EdgeInsets.fromLTRB(6, 6, 6, 6),
            child: Row(children: [
              IconButton(icon: const Icon(Icons.image), onPressed: sending ? null : sendImage),
              Expanded(
                child: TextField(
                  controller: ctrl,
                  minLines: 1,
                  maxLines: 4,
                  textCapitalization: TextCapitalization.sentences,
                  decoration: InputDecoration(
                    hintText: 'Message',
                    filled: true,
                    fillColor: Colors.white,
                    contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
                    border: OutlineInputBorder(borderRadius: BorderRadius.circular(24), borderSide: BorderSide.none),
                  ),
                ),
              ),
              const SizedBox(width: 6),
              CircleAvatar(backgroundColor: const Color(0xFF25D366), child: IconButton(icon: const Icon(Icons.send, color: Colors.white, size: 20), onPressed: sending ? null : send)),
            ]),
          ),
        ),
      ]),
    );
  }

  Widget _bubble(int i) {
    final m = msgs[i];
    final mine = (m['fromAdmin'] == true) == widget.admin;
    final date = DateTime.parse(m['sentAt']).toLocal();
    final prev = i > 0 ? DateTime.parse(msgs[i - 1]['sentAt']).toLocal() : null;
    final newDay = prev == null || prev.day != date.day || prev.month != date.month || prev.year != date.year;
    final read = mine && (m['id'] as int) <= readUpTo;
    return Column(children: [
      if (newDay)
        Container(
          margin: const EdgeInsets.symmetric(vertical: 8),
          padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 2),
          decoration: BoxDecoration(color: const Color(0xFFE1F2FB), borderRadius: BorderRadius.circular(8)),
          child: Text(fdate(m['sentAt']), style: const TextStyle(fontSize: 12)),
        ),
      Align(
        alignment: mine ? Alignment.centerRight : Alignment.centerLeft,
        child: Container(
          constraints: BoxConstraints(maxWidth: MediaQuery.of(context).size.width * 0.78),
          margin: const EdgeInsets.symmetric(vertical: 2),
          padding: const EdgeInsets.fromLTRB(10, 6, 10, 4),
          decoration: BoxDecoration(color: mine ? const Color(0xFFD9FDD3) : Colors.white, borderRadius: BorderRadius.circular(10)),
          child: Column(crossAxisAlignment: CrossAxisAlignment.end, mainAxisSize: MainAxisSize.min, children: [
            if (m['attachment'] != null)
              GestureDetector(
                onTap: () => openUrl(Api.url(m['attachment'])),
                child: Padding(
                  padding: const EdgeInsets.only(bottom: 4),
                  child: ClipRRect(borderRadius: BorderRadius.circular(6), child: Image.network(Api.url(m['attachment']), width: 220, fit: BoxFit.cover)),
                ),
              ),
            if (m['text'] != null && '${m['text']}'.isNotEmpty) Align(alignment: Alignment.centerLeft, child: Text('${m['text']}')),
            Row(mainAxisSize: MainAxisSize.min, children: [
              Text(_time(m['sentAt']), style: const TextStyle(fontSize: 11, color: Colors.black54)),
              if (mine) ...[
                const SizedBox(width: 3),
                Icon(Icons.done_all, size: 15, color: read ? const Color(0xFF53BDEB) : Colors.grey),
              ],
            ]),
          ]),
        ),
      ),
    ]);
  }
}

class StudentChatScreen extends StatelessWidget {
  const StudentChatScreen({super.key});
  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(title: const Text('Admin se chat')),
        body: const ChatView(path: '/api/student/chat', admin: false, activeKey: 'student'),
      );
}

class AdminChatScreen extends StatefulWidget {
  final int studentId;
  final String? name;
  const AdminChatScreen({super.key, required this.studentId, this.name});
  @override
  State<AdminChatScreen> createState() => _AdminChatScreenState();
}

class _AdminChatScreenState extends State<AdminChatScreen> {
  Map? st;
  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(
          title: Text(st != null ? '${st!['name']} (${st!['rollNo']})' : (widget.name ?? 'Chat')),
          actions: [
            if (st != null) IconButton(icon: const Icon(Icons.call), onPressed: () => openUrl('tel:${st!['mobile']}')),
          ],
        ),
        body: ChatView(
          path: '/api/admin/chats/${widget.studentId}',
          admin: true,
          activeKey: 'admin:${widget.studentId}',
          onStudent: (s) { if (st == null && mounted) setState(() => st = s); },
        ),
      );
}

/// Admin ke liye sabhi chats ki list (Chats tab)
class ChatsTab extends StatefulWidget {
  const ChatsTab({super.key});
  @override
  State<ChatsTab> createState() => _ChatsTabState();
}

class _ChatsTabState extends State<ChatsTab> {
  List rows = [];
  bool loading = true;
  Timer? timer;
  StreamSubscription? sub;

  @override
  void initState() {
    super.initState();
    load();
    timer = Timer.periodic(const Duration(seconds: 8), (_) => load());
    sub = Push.bus.stream.listen((_) => load());
  }

  @override
  void dispose() { timer?.cancel(); sub?.cancel(); super.dispose(); }

  Future<void> load() async {
    try {
      final r = await Api.get('/api/admin/chats');
      if (mounted) setState(() { rows = r; loading = false; });
    } catch (_) { if (mounted) setState(() => loading = false); }
  }

  Future<void> broadcast() async {
    List batches = [];
    try { batches = await Api.get('/api/admin/batches'); } catch (_) {}
    int batchId = 0;
    final text = TextEditingController();
    if (!mounted) return;
    final ok = await showDialog<bool>(context: context, builder: (_) => StatefulBuilder(builder: (ctx, set) => AlertDialog(
      title: const Text('Announcement bhejein'),
      content: SingleChildScrollView(child: Column(mainAxisSize: MainAxisSize.min, children: [
        DropdownButtonFormField<int>(
          value: batchId,
          items: [const DropdownMenuItem(value: 0, child: Text('Sabhi active students')), for (final b in batches) DropdownMenuItem(value: b['id'] as int, child: Text('${b['name']}'))],
          onChanged: (v) => set(() => batchId = v!),
          decoration: const InputDecoration(labelText: 'Kisko'),
        ),
        TextField(controller: text, maxLines: 4, decoration: const InputDecoration(labelText: 'Message')),
      ])),
      actions: [TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('Cancel')), FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('Bhejein'))],
    )));
    if (ok != true || text.text.trim().isEmpty) return;
    try {
      final r = await Api.post('/api/admin/broadcast', {'batchId': batchId, 'text': text.text.trim()});
      if (mounted) toast(context, '${r['sent']} students ko bhej diya');
      load();
    } catch (e) { if (mounted) toast(context, e.toString(), error: true); }
  }

  Future<void> remind() async {
    final ok = await showDialog<bool>(context: context, builder: (_) => AlertDialog(
      title: const Text('Due reminder'),
      content: const Text('Jin students ki fees due hai un sabko personal message + notification jayega.'),
      actions: [TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')), FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Bhejein'))],
    ));
    if (ok != true) return;
    try {
      final r = await Api.post('/api/admin/dues/remind', {});
      if (mounted) toast(context, '${r['sent']} students ko reminder gaya');
      load();
    } catch (e) { if (mounted) toast(context, e.toString(), error: true); }
  }

  @override
  Widget build(BuildContext context) {
    if (loading) return const Loader();
    return Scaffold(
      floatingActionButton: Column(mainAxisSize: MainAxisSize.min, children: [
        FloatingActionButton.small(heroTag: 'rem', onPressed: remind, tooltip: 'Due reminder', child: const Icon(Icons.notifications_active)),
        const SizedBox(height: 8),
        FloatingActionButton.extended(heroTag: 'bc', onPressed: broadcast, icon: const Icon(Icons.campaign), label: const Text('Broadcast')),
      ]),
      body: RefreshIndicator(
        onRefresh: load,
        child: rows.isEmpty
            ? ListView(children: const [Padding(padding: EdgeInsets.all(40), child: Center(child: Text('Abhi koi chat nahi.\nStudent detail se "Message" dabakar shuru karein.', textAlign: TextAlign.center)))])
            : ListView.separated(
                padding: const EdgeInsets.only(bottom: 140),
                itemCount: rows.length,
                separatorBuilder: (_, __) => const Divider(height: 1),
                itemBuilder: (_, i) {
                  final r = rows[i];
                  final unread = (r['unread'] ?? 0) as int;
                  return ListTile(
                    leading: CircleAvatar(backgroundImage: r['photo'] != null ? NetworkImage(Api.url(r['photo'])) : null, child: r['photo'] == null ? Text('${r['name']}'[0]) : null),
                    title: Text(r['name'], style: TextStyle(fontWeight: unread > 0 ? FontWeight.bold : FontWeight.normal)),
                    subtitle: Text('${r['lastFromAdmin'] == true ? 'You: ' : ''}${r['lastText']}', maxLines: 1, overflow: TextOverflow.ellipsis),
                    trailing: Column(mainAxisAlignment: MainAxisAlignment.center, crossAxisAlignment: CrossAxisAlignment.end, children: [
                      Text(_time(r['lastAt']), style: const TextStyle(fontSize: 12)),
                      if (unread > 0) Container(margin: const EdgeInsets.only(top: 4), padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 2), decoration: BoxDecoration(color: const Color(0xFF25D366), borderRadius: BorderRadius.circular(10)), child: Text('$unread', style: const TextStyle(color: Colors.white, fontSize: 12))),
                    ]),
                    onTap: () async {
                      await Navigator.push(context, MaterialPageRoute(builder: (_) => AdminChatScreen(studentId: r['studentId'], name: r['name'])));
                      load();
                    },
                  );
                },
              ),
      ),
    );
  }
}
