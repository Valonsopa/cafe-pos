import 'dart:convert';
import 'package:flutter/material.dart';
import 'package:http/http.dart' as http;

void main() => runApp(const CafePosApp());

class CafePosApp extends StatelessWidget {
  const CafePosApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'Cafe POS Mobile',
      theme: ThemeData(useMaterial3: true),
      home: const HomeScreen(),
    );
  }
}

class MenuItem {
  final String name;
  final int priceCents;
  const MenuItem(this.name, this.priceCents);
}

class CartLine {
  final MenuItem item;
  int qty;
  CartLine(this.item, this.qty);
}

class HomeScreen extends StatefulWidget {
  const HomeScreen({super.key});
  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> {
  // ✅ CHANGE THIS to your PC IPv4, example: http://192.168.1.50:5055
  String baseUrl = "http://192.168.1.22:5055";

  String tableLabel = "T1";
  final String deviceId = "phone-01";
  final String userId = "worker-01";

  final List<MenuItem> menu = const [
    MenuItem("Espresso", 120),
    MenuItem("Cappuccino", 180),
    MenuItem("Water", 80),
    MenuItem("Beer 0.5L", 250),
  ];

  final Map<String, CartLine> cart = {};

  void add(MenuItem item) {
    setState(() {
      cart.putIfAbsent(item.name, () => CartLine(item, 0));
      cart[item.name]!.qty += 1;
    });
  }

  void remove(MenuItem item) {
    setState(() {
      if (!cart.containsKey(item.name)) return;
      cart[item.name]!.qty -= 1;
      if (cart[item.name]!.qty <= 0) cart.remove(item.name);
    });
  }

  int get totalCents {
    var sum = 0;
    for (final line in cart.values) {
      sum += line.item.priceCents * line.qty;
    }
    return sum;
  }

  Future<void> sendOrder() async {
    if (cart.isEmpty) {
      showSnack("Cart is empty");
      return;
    }

    final idempotencyKey = "$deviceId-${DateTime.now().millisecondsSinceEpoch}";

    final body = {
      "IdempotencyKey": idempotencyKey,
      "DeviceId": deviceId,
      "UserId": userId,
      "Source": "TABLE",
      "TableLabel": tableLabel,
      "TicketLabel": null,
      "Items": cart.values.map((line) {
        return {
          "Name": line.item.name,
          "UnitPriceCents": line.item.priceCents,
          "Qty": line.qty,
          "TaxRateBps": 0, // ✅ TAX INCLUDED
          "Note": null
        };
      }).toList(),
    };

    try {
      final res = await http.post(
        Uri.parse("$baseUrl/api/orders/add-items"),
        headers: {"Content-Type": "application/json"},
        body: jsonEncode(body),
      );

      if (res.statusCode == 200) {
        final data = jsonDecode(res.body);
        setState(() => cart.clear());
        showSnack("Sent! Order #${data["orderNumber"]}");
      } else {
        showSnack("Error ${res.statusCode}: ${res.body}");
      }
    } catch (e) {
      showSnack("Network error: $e");
    }
  }

  void showSnack(String msg) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(msg)));
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text("Cafe POS — Worker"),
        actions: [
          IconButton(
            icon: const Icon(Icons.settings),
            onPressed: () async {
              final newUrl = await showDialog<String>(
                context: context,
                builder: (_) => ServerDialog(initial: baseUrl),
              );
              if (newUrl != null && newUrl.trim().isNotEmpty) {
                setState(() => baseUrl = newUrl.trim());
              }
            },
          )
        ],
      ),
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.all(12),
            child: Row(
              children: [
                const Text("Table: "),
                const SizedBox(width: 8),
                DropdownButton<String>(
                  value: tableLabel,
                  items: ["T1", "T2", "T3", "T4", "T5"]
                      .map((t) => DropdownMenuItem(value: t, child: Text(t)))
                      .toList(),
                  onChanged: (v) => setState(() => tableLabel = v ?? "T1"),
                ),
                const Spacer(),
                Text("Server: $baseUrl", style: const TextStyle(fontSize: 11, color: Colors.grey)),
              ],
            ),
          ),
          Expanded(
            child: GridView.count(
              padding: const EdgeInsets.all(12),
              crossAxisCount: 2,
              crossAxisSpacing: 10,
              mainAxisSpacing: 10,
              children: menu.map((item) {
                final qty = cart[item.name]?.qty ?? 0;
                return Card(
                  child: InkWell(
                    onTap: () => add(item),
                    child: Padding(
                      padding: const EdgeInsets.all(12),
                      child: Column(
                        mainAxisAlignment: MainAxisAlignment.center,
                        children: [
                          Text(item.name, style: const TextStyle(fontSize: 18, fontWeight: FontWeight.bold)),
                          const SizedBox(height: 6),
                          Text("${(item.priceCents / 100).toStringAsFixed(2)}"),
                          const SizedBox(height: 10),
                          Text("Qty: $qty"),
                          const SizedBox(height: 8),
                          OutlinedButton(
                            onPressed: () => remove(item),
                            child: const Text("−"),
                          )
                        ],
                      ),
                    ),
                  ),
                );
              }).toList(),
            ),
          ),
          Container(
            padding: const EdgeInsets.all(12),
            decoration: const BoxDecoration(border: Border(top: BorderSide(color: Color(0xFFE0E0E0)))),
            child: Row(
              children: [
                Text("Total: ${(totalCents / 100).toStringAsFixed(2)}",
                    style: const TextStyle(fontSize: 18, fontWeight: FontWeight.bold)),
                const Spacer(),
                ElevatedButton.icon(
                  onPressed: sendOrder,
                  icon: const Icon(Icons.send),
                  label: const Text("Send Order"),
                )
              ],
            ),
          )
        ],
      ),
    );
  }
}

class ServerDialog extends StatefulWidget {
  final String initial;
  const ServerDialog({super.key, required this.initial});

  @override
  State<ServerDialog> createState() => _ServerDialogState();
}

class _ServerDialogState extends State<ServerDialog> {
  late final TextEditingController _c;
  @override
  void initState() {
    super.initState();
    _c = TextEditingController(text: widget.initial);
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: const Text("Server URL"),
      content: TextField(
        controller: _c,
        decoration: const InputDecoration(hintText: "http://192.168.1.50:5055"),
      ),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context), child: const Text("Cancel")),
        ElevatedButton(onPressed: () => Navigator.pop(context, _c.text), child: const Text("Save")),
      ],
    );
  }
}
