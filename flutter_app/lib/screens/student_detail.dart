import 'package:flutter/material.dart';
import '../api.dart';
import '../ui.dart';
import 'admin_shell.dart' show openUrl;
import 'admission_form.dart';
import 'chat.dart';

class StudentDetail extends StatefulWidget {
  final int id;
  const StudentDetail({super.key, required this.id});
  @override
  State<StudentDetail> createState() => _StudentDetailState();
}

class _StudentDetailState extends State<StudentDetail> {
  Map? d;
  String? err;
  @override
  void initState() { super.initState(); load(); }

  Future<void> load() async {
    try {
      final r = await Api.get('/api/admin/students/${widget.id}');
      if (mounted) setState(() { d = r; err = null; });
    } catch (e) { if (mounted) setState(() => err = e.toString()); }
  }

  Future<void> act(Future<dynamic> Function() f) async {
    try { await f(); await load(); } catch (e) { if (mounted) toast(context, e.toString(), error: true); }
  }

  Future<void> collect() async {
    final sum = d!['summary'];
    final amt = TextEditingController(text: '${(sum['overdueNow'] as num) > 0 ? sum['overdueNow'] : ''}');
    final ref = TextEditingController();
    String mode = 'Cash';
    final ok = await showDialog<bool>(context: context, builder: (_) => StatefulBuilder(builder: (ctx, set) => AlertDialog(
      title: const Text('Fees receive karein'),
      content: Column(mainAxisSize: MainAxisSize.min, children: [
        Text('Remaining ${money(sum['balance'])} • Due ${money(sum['overdueNow'])}'),
        TextField(controller: amt, keyboardType: TextInputType.number, decoration: const InputDecoration(labelText: 'Amount ₹')),
        DropdownButtonFormField<String>(
          value: mode,
          items: [for (final m in ['Cash', 'UPI', 'Card', 'Bank', 'Cheque']) DropdownMenuItem(value: m, child: Text(m))],
          onChanged: (v) => set(() => mode = v!),
          decoration: const InputDecoration(labelText: 'Mode'),
        ),
        TextField(controller: ref, decoration: const InputDecoration(labelText: 'Reference / UTR (optional)')),
      ]),
      actions: [TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('Cancel')), FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('Receive'))],
    )));
    if (ok != true) return;
    try {
      final r = await Api.post('/api/admin/students/${widget.id}/payments', {'amount': num.tryParse(amt.text) ?? 0, 'mode': mode, 'reference': ref.text.trim()});
      await load();
      if (!mounted) return;
      toast(context, 'Payment ho gayi, student ko thank-you notification gaya: ${r['receiptNo']}');
      openUrl(Api.printUrl('/receipt/${r['id']}'));
    } catch (e) { if (mounted) toast(context, e.toString(), error: true); }
  }

  // Settlement / round-off: kuch amount lekar baaki maaf, hisab close
  Future<void> settle() async {
    String scope = 'Overdue', mode = 'Cash';
    bool close = false;
    num bal = 0;
    try {
      bal = (await Api.get('/api/admin/students/${widget.id}/settle-preview?scope=$scope'))['balance'];
    } catch (e) { if (mounted) toast(context, e.toString(), error: true); return; }
    final pay = TextEditingController(text: '$bal');
    final remarks = TextEditingController();
    if (!mounted) return;
    final ok = await showDialog<bool>(context: context, builder: (_) => StatefulBuilder(builder: (ctx, set) {
      final payNow = num.tryParse(pay.text) ?? 0;
      return AlertDialog(
        title: const Text('Settlement / Round-off'),
        content: SingleChildScrollView(
          child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
            DropdownButtonFormField<String>(
              value: scope,
              items: const [
                DropdownMenuItem(value: 'Overdue', child: Text('Sirf abhi tak ki due')),
                DropdownMenuItem(value: 'All', child: Text('Poora baaki (future bhi)')),
              ],
              onChanged: (v) async {
                set(() => scope = v!);
                try {
                  final r = await Api.get('/api/admin/students/${widget.id}/settle-preview?scope=$scope');
                  set(() { bal = r['balance']; pay.text = '$bal'; });
                } catch (_) {}
              },
              decoration: const InputDecoration(labelText: 'Kya settle karein'),
            ),
            const SizedBox(height: 8),
            Text('Baaki: ${money(bal)}', style: const TextStyle(fontWeight: FontWeight.bold)),
            TextField(controller: pay, keyboardType: TextInputType.number, onChanged: (_) => set(() {}), decoration: const InputDecoration(labelText: 'Student ab kitna dega ₹')),
            Wrap(spacing: 6, children: [
              for (final step in [10, 100, 500, 1000])
                ActionChip(label: Text('↓$step'), onPressed: () => set(() => pay.text = '${((bal / step).floor() * step)}')),
              ActionChip(label: const Text('Poora lo'), onPressed: () => set(() => pay.text = '$bal')),
              ActionChip(label: const Text('Poora maaf'), onPressed: () => set(() => pay.text = '0')),
            ]),
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 8),
              child: Text('Maaf hoga: ${money((bal - payNow) < 0 ? 0 : bal - payNow)}', style: const TextStyle(color: Colors.red, fontWeight: FontWeight.bold)),
            ),
            DropdownButtonFormField<String>(
              value: mode,
              items: [for (final m in ['Cash', 'UPI', 'Card', 'Bank', 'Cheque']) DropdownMenuItem(value: m, child: Text(m))],
              onChanged: (v) => set(() => mode = v!),
              decoration: const InputDecoration(labelText: 'Payment mode'),
            ),
            TextField(controller: remarks, decoration: const InputDecoration(labelText: 'Remarks / karan')),
            SwitchListTile(contentPadding: EdgeInsets.zero, title: const Text('Account close (Completed)'), value: close, onChanged: (v) => set(() => close = v)),
          ]),
        ),
        actions: [TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('Cancel')), FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('Settle karein'))],
      );
    }));
    if (ok != true) return;
    try {
      await Api.post('/api/admin/students/${widget.id}/settle', {
        'scope': scope, 'payNow': num.tryParse(pay.text) ?? 0, 'mode': mode,
        'remarks': remarks.text.trim(), 'closeAccount': close,
      });
      await load();
      if (mounted) toast(context, 'Settlement ho gaya, student ko message gaya');
    } catch (e) { if (mounted) toast(context, e.toString(), error: true); }
  }

  Future<void> cancelPay(Map p) async {
    final ok = await showDialog<bool>(context: context, builder: (_) => AlertDialog(
      title: Text('Cancel ${p['receiptNo']}?'),
      content: Text('${money(p['amount'])} ki payment cancel ho jayegi.'),
      actions: [TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('No')), FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Yes'))],
    ));
    if (ok == true) act(() => Api.post('/api/admin/payments/${p['id']}/cancel'));
  }

  Future<void> cancelSettle(Map x) async {
    final ok = await showDialog<bool>(context: context, builder: (_) => AlertDialog(
      title: const Text('Settlement cancel?'),
      content: Text('${money(x['waivedAmount'])} wapas due ho jayega.'),
      actions: [TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('No')), FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Yes'))],
    ));
    if (ok == true) act(() => Api.post('/api/admin/settlements/${x['id']}/cancel'));
  }

  @override
  Widget build(BuildContext context) {
    if (err != null) return Scaffold(appBar: AppBar(), body: ErrorView(err!, load));
    if (d == null) return Scaffold(appBar: AppBar(), body: const Loader());
    final s = d!['student'], sum = d!['summary'];
    final fees = d!['fees'] as List, pays = d!['payments'] as List, sets = (d!['settlements'] ?? []) as List;
    final active = s['status'] == 'Active';
    final hasBalance = (sum['balance'] as num) > 0;
    return Scaffold(
      appBar: AppBar(title: Text(s['name']), actions: [
        IconButton(
          icon: const Icon(Icons.edit),
          onPressed: () async {
            final ok = await Navigator.push(context, MaterialPageRoute(builder: (_) => AdmissionForm(student: s)));
            if (ok == true) load();
          },
        ),
        IconButton(icon: const Icon(Icons.chat), tooltip: 'Message', onPressed: () => Navigator.push(context, MaterialPageRoute(builder: (_) => AdminChatScreen(studentId: s['id'], name: s['name'])))),
        IconButton(icon: const Icon(Icons.badge), tooltip: 'ID Card', onPressed: () => openUrl(Api.printUrl('/idcard/student/${s['id']}'))),
      ]),
      floatingActionButton: active && hasBalance ? FloatingActionButton.extended(onPressed: collect, icon: const Icon(Icons.payments), label: const Text('Receive fees')) : null,
      body: RefreshIndicator(
        onRefresh: load,
        child: ListView(padding: const EdgeInsets.fromLTRB(12, 12, 12, 90), children: [
          Card(
            child: ListTile(
              leading: CircleAvatar(radius: 28, backgroundImage: s['photoPath'] != null ? NetworkImage(Api.url(s['photoPath'])) : null, child: s['photoPath'] == null ? Text('${s['name']}'[0]) : null),
              title: Text('${s['name']}  •  Roll ${s['rollNo']}'),
              subtitle: Text('S/o ${s['fatherName']}\n${s['category']} › ${s['batch']} (${s['course']})\n📞 ${s['mobile']}  •  ${s['status']}\nDiscount: ${money(s['monthlyDiscount'])}/month, ${money(s['admissionDiscount'])} admission'),
              isThreeLine: true,
            ),
          ),
          if (s['status'] == 'Pending')
            FilledButton.icon(onPressed: () => act(() => Api.post('/api/admin/students/${s['id']}/approve')), icon: const Icon(Icons.check), label: const Text('Approve admission')),
          if (active)
            TextButton(onPressed: () => act(() => Api.post('/api/admin/students/${s['id']}/status', {'status': 'Left'})), child: const Text('Mark as Left')),
          if (s['status'] == 'Left' || s['status'] == 'Completed')
            TextButton(onPressed: () => act(() => Api.post('/api/admin/students/${s['id']}/status', {'status': 'Active'})), child: const Text('Re-activate')),
          GridView.count(crossAxisCount: 2, shrinkWrap: true, physics: const NeverScrollableScrollPhysics(), childAspectRatio: 2.4, children: [
            Stat('Total payable', money(sum['totalNet'])),
            Stat('Paid', money(sum['paid']), color: Colors.green),
            Stat('Remaining', money(sum['balance'])),
            Stat('Due abhi (${sum['overdueCount']} months)', money(sum['overdueNow']), color: Colors.red),
          ]),
          if (active && hasBalance)
            OutlinedButton.icon(onPressed: settle, icon: const Icon(Icons.calculate), label: const Text('Settlement / Round-off (hisab close)')),
          const Padding(padding: EdgeInsets.symmetric(vertical: 8), child: Text('Fee schedule', style: TextStyle(fontWeight: FontWeight.bold))),
          for (final f in fees)
            Card(
              color: (f['balance'] as num) == 0 ? Colors.green.shade50 : (f['isOverdue'] == true ? Colors.red.shade50 : null),
              child: ListTile(
                dense: true,
                title: Text(f['title']),
                subtitle: Text('Due ${fdate(f['dueDate'])} • ${money(f['amount'])} − ${money(f['discount'])} disc${(f['waived'] ?? 0) > 0 ? ' − ${money(f['waived'])} maaf' : ''}'),
                trailing: Text((f['balance'] as num) == 0 ? 'PAID' : money(f['balance']),
                    style: TextStyle(fontWeight: FontWeight.bold, color: (f['balance'] as num) == 0 ? Colors.green : Colors.red)),
              ),
            ),
          const Padding(padding: EdgeInsets.symmetric(vertical: 8), child: Text('Payment history', style: TextStyle(fontWeight: FontWeight.bold))),
          for (final p in pays)
            ListTile(
              dense: true,
              title: Text('${p['receiptNo']} • ${money(p['amount'])}${p['isCancelled'] == true ? ' (cancelled)' : ''}',
                  style: TextStyle(decoration: p['isCancelled'] == true ? TextDecoration.lineThrough : null)),
              subtitle: Text('${fdate(p['paidOn'])} • ${p['mode']}'),
              onTap: () => openUrl(Api.printUrl('/receipt/${p['id']}')),
              trailing: p['isCancelled'] == true ? null : IconButton(icon: const Icon(Icons.cancel_outlined, color: Colors.red), onPressed: () => cancelPay(p)),
            ),
          if (sets.isNotEmpty) ...[
            const Padding(padding: EdgeInsets.symmetric(vertical: 8), child: Text('Settlement history', style: TextStyle(fontWeight: FontWeight.bold))),
            for (final x in sets)
              ListTile(
                dense: true,
                title: Text('Diya ${money(x['paidAmount'])} • Maaf ${money(x['waivedAmount'])}${x['isCancelled'] == true ? ' (cancelled)' : ''}',
                    style: TextStyle(decoration: x['isCancelled'] == true ? TextDecoration.lineThrough : null)),
                subtitle: Text('${fdate(x['settledOn'])} • Baaki tha ${money(x['balanceBefore'])}${(x['remarks'] ?? '').toString().isNotEmpty ? '\n${x['remarks']}' : ''}'),
                trailing: x['isCancelled'] == true ? null : IconButton(icon: const Icon(Icons.undo, color: Colors.red), onPressed: () => cancelSettle(x)),
              ),
          ],
        ]),
      ),
    );
  }
}
