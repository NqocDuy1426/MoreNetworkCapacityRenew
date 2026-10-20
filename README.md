# More Network Capacity Renew

[![Steam Workshop](https://img.shields.io/badge/Steam_Workshop-3818403598-blue.svg?logo=steam)](https://steamcommunity.com/sharedfiles/filedetails/?id=3818403598)
[![Platform](https://img.shields.io/badge/Platform-macOS%20%7C%20Windows-brightgreen.svg)]()
[![Game](https://img.shields.io/badge/Cities%3A%20Skylines-1.x-orange.svg)]()
[![Dependency](https://img.shields.io/badge/Dependency-Harmony%202.2%2B-blueviolet.svg)](https://steamcommunity.com/workshop/filedetails/?id=2040656402)
[![License](https://img.shields.io/badge/License-MIT-lightgrey.svg)]()

> **The Definitive 64K Network Limit Breaker for Cities: Skylines.**  
> Fully rewritten, stabilized, and optimized for modern platforms, native macOS (Apple Silicon & Intel), and full TM:PE integration.

📖 **Languages:** [English](#-english) • [Tiếng Việt](#-tiếng-việt)

---

## 🌐 English

### Overview: What is this mod?
Vanilla *Cities: Skylines* caps road and network creation at ~32,000 nodes and segments. Once this threshold is reached, the game displays the notorious `"Cannot create more objects of this type"` error, halting all city expansion.

**More Network Capacity Renew** safely elevates this internal engine ceiling to **65,536 (64K) Nodes and Segments**, providing double the building freedom. 

Unlike previous experimental or unmaintained releases that suffered from severe crash loops and broken traffic routing, **Renew** is engineered from the ground up for **rock-solid stability, accurate traffic simulation, and zero savegame data loss.**

---

### 🚨 What's New in v4.3.39 (Complete 65K TM:PE Scope Expansion & Zero-Interference Simulation)
- **Full 65K TM:PE Influence & Scope Expansion:**
  - TM:PE's operational range is fully expanded across all 65,024 segments and 65,024 nodes (up from vanilla 36,864 / 32,768 limits).
  - Dynamic IL transpilation safely updates 56+ critical constant boundaries and buffer allocations inside TM:PE without modifying or interfering with its native traffic simulation logic.
  - Complete support across all TM:PE tools & overlays: Lane Connector, Priority Signs, Speed Limits, Junction Restrictions, Timed Traffic Lights, and Vehicle Restrictions across the entire 65K network.
  - Full automatic recalculation across all 65K segments for `RoutingManager`, `SpeedLimitManager`, and `JunctionRestrictionsManager`.
- **SegmentEnd Resolution & Vehicle Flow Fix:**
  - Fixed split-array indexing in `SegmentEndManager.GetIndex`, `TurnOnRedManager`, and `TrafficMeasurementManager` (offset 65,024). Eliminates vehicles stopping dead or freezing at high-ID junctions due to missing segment end lookups.
  - Zero external vehicle deletion/watchdogs: 100% pure native traffic simulation that strictly respects user-configured TM:PE despawn preferences (including *Disable Despawning*).
- **Pure Native Multi-Threaded Pathfinding & Vehicle Dispatch:**
  - TM:PE `CustomPathFind` worker threads run 100% natively at 60+ FPS without external hook overhead or thread synchronization locks.
  - Buildings continuously dispatch service and cargo vehicles without delay, even after mass traffic despawns.
- **Modern Mod Compatibility Suite:**
  - **Transfer Manager Community Edition (TransferManagerCE):** Dynamic BitArray 65K path-distance expansion.
  - **Rebalanced Industries Revisited 1.0.3:** Harmonious warehouse and cargo transport calculations.
  - **Adaptive Roads (ADR):** Dual-stream buffer deserialization armor (prevents savegame load EOF crashes).
  - **More Vehicles & More PathUnits x2:** Synchronized 65,536 vehicle buffer and 524,288 path units.

---

### ⚡ Key Advantages & Superior Features

1. **True 64K Expanded Capacity:**
   - Doubles maximum node and segment capacity to 65,536.
   - Build extensive regional passenger & cargo rail, complex multi-level highway interchanges, and expansive subway grids without restriction.

2. **Native Apple Silicon & Intel Mac Stability:**
   - Eliminates hard memory alignment overflows and engine crash loops on macOS (M1/M2/M3/M4 & Intel).
   - Dynamic IL transpilers safely bypass hardcoded array boundary checks in vanilla routines and third-party dependencies.

3. **Complete TM:PE (Traffic Manager) Compatibility:**
   - **Timed Traffic Lights (TTL):** 100% operational on high-ID nodes (> 32,768).
   - **Custom Lanes & Connectors:** Lane arrows, lane connections, custom speed limits, and priority rules are preserved cleanly.
   - **Zero Deserialization Errors:** Restores complete savegame persistence for all custom TM:PE junctions.

4. **Accurate Traffic Simulation & Heatmaps:**
   - Real-time traffic flow color refresh (Traffic Flow Color Map) on all extended segments.
   - Active simulation loop prevents the notorious "frozen vehicles" or phantom deadlock bug at high-ID junctions.

5. **Safe Savegame Persistence Guarantee:**
   - Clean savegame serialization ensures your custom traffic setups, timed nodes, and networks reload seamlessly without data loss.

6. **All-In-One Standalone Architecture:**
   - Independent mod package. Does not require secondary "fix" mods or complex manual patching.

---

### 🧩 Compatibility Matrix

Fully tested and compatible with the modern modding ecosystem:
- **Harmony 2.2+** *(Required Dependency)*
- **TM:PE (Traffic Manager: President Edition)** *(Stable & Labs)*
- **81 Tiles 2**
- **Move It**
- **Node Controller Renewal**
- **Intersection Marking Tool (IMT)**
- **Network Multitool**
- **Adaptive Roads (ADR)**
- **Network Skins**

---

### 🛠️ Repository Structure & Building

```text
├── MoreNetworkCapacity/               # Core engine implementation
├── MoreNetworkCapacity.Compatibility/ # Compatibility adapters (TM:PE, 81 Tiles, ADR)
├── MoreNetworkCapacity.Patches/       # Harmony runtime patches & IL transpilers
├── Renew.cs                           # Unified mod entry point, simulation loops & fixes
├── strip_iusermod.cs                  # Automation tool to strip IUserMod from base DLL
├── build.sh                           # 1-click build & deployment script
└── preview-image.png                  # Workshop thumbnail asset
```

To compile locally on macOS/Linux:
```bash
./build.sh
```
The script compiles the assembly with `Mono` / `mcs`, strips redundant interfaces, and deploys directly to your game's `Addons/Mods/MoreNetworkCapacityRenew` and Workshop directories.

---

### 💡 Best Practices & Safety

* **Backup Your Saves:** Always maintain regular backup copies of your savegames before testing limit-breaking mods.
* **Persistent Saves:** Once a city exceeds vanilla network limits (> 32,768 nodes/segments), this mod must remain enabled for the game to properly load and manage those extended networks.

---
---

## 🇻🇳 Tiếng Việt

### Giới thiệu: Bản mod này là gì?
Mặc định *Cities: Skylines* giới hạn số lượng hạ tầng mạng lưới đường sá ở mức ~32,000 node và segment. Khi chạm ngưỡng này, trò chơi sẽ thông báo lỗi `"Cannot create more objects of this type"`, khiến người chơi không thể đặt thêm đường xá hay đường sắt.

**More Network Capacity Renew** phá vỡ rào cản này một cách an toàn, nâng tổng sức chứa lên **65,536 (64K) Node & Segment** - nhân đôi không gian xây dựng để bạn thỏa sức kiến thiết các siêu đô thị triệu dân.

Khác biệt hoàn toàn so với các bản thử nghiệm cũ dễ gây sập game và lỗi mô phỏng giao thông, phiên bản **Renew** được xây dựng lại toàn diện với tiêu chí: **độ ổn định tuyệt đối, mô phỏng giao thông chuẩn xác và không mất dữ liệu savegame.**

---

### 🚨 Điểm mới trong bản cập nhật v4.3.39 (Mở rộng Toàn diện Phạm vi TM:PE lên 65K & Mô phỏng Giao thông Chuẩn xác)
- **Mở rộng Toàn diện Phạm vi Ảnh hưởng của TM:PE lên 65,024 Segments & Nodes:**
  - Mở rộng toàn bộ tầm phủ sóng của TM:PE từ giới hạn gốc (36,864 segments và 32,768 nodes) lên chuẩn 65,024 segments và 65,024 nodes trên toàn bộ bản đồ thành phố.
  - Cơ chế Dynamic IL Transpiler tự động cập nhật hơn 56 điểm giới hạn cứng và phân bổ bộ nhớ nội bộ của TM:PE mà tuyệt đối không can thiệp hay làm biến dạng thuật toán mô phỏng tự nhiên của TM:PE.
  - Hỗ trợ đầy đủ toàn bộ thanh công cụ TM:PE: Nối làn (Lane Connector), Biển ưu tiên (Priority Signs), Tốc độ (Speed Limits), Luật nút giao (Junction Restrictions), Đèn đếm giây (Timed Traffic Lights) và Cấm phương tiện (Vehicle Restrictions) trên mọi con đường mở rộng.
  - Tự động đồng bộ và tính toán lại luồng giao thông 65K trong `RoutingManager`, `SpeedLimitManager` và `JunctionRestrictionsManager`.
- **Khắc phục Triệt để Lỗi Xe Dừng Khựng Giữa Đường / Tại Nút Giao:**
  - Sửa lỗi định vị địa chỉ mảng đôi `SegmentEndManager.GetIndex`, `TurnOnRedManager` và `TrafficMeasurementManager` (offset chuẩn 65,024). Khắc phục tình trạng TM:PE trả về `null` điểm cuối nút giao khiến xe không biết rẽ và đứng chôn chân giữa đường.
  - Không xoá xe cưỡng chế: Trả lại 100% quyền điều tiết và despawn cho game gốc cùng cài đặt của người dùng trong TM:PE (tôn trọng tuyệt đối tuỳ chọn *Disable Despawning*).
- **Tìm đường Đa luồng Native 100% & Xuất xe Tự nhiên:**
  - Luồng tìm đường `CustomPathFind` của TM:PE chạy 100% native tốc độ cao 60+ FPS, không bị khóa luồng hay chậm trễ.
  - Toà nhà liên tục sản sinh và xuất xe dịch vụ, xe rác, cứu hoả, chở hàng mượt mà, không bị kẹt trong toà nhà sau khi dọn giao thông.
- **Tương thích Toàn diện Hệ sinh thái Mod Hiện đại:**
  - **Transfer Manager Community Edition (TransferManagerCE):** Mở rộng mảng BitArray 65K cho thuật toán tìm đường Dijkstra.
  - **Rebalanced Industries Revisited 1.0.3:** Vận hành trơn tru các kho hàng công nghiệp và chuỗi cung ứng.
  - **Adaptive Roads (ADR):** Giáp bảo vệ đọc file save đa luồng, chống lỗi văng game EOF khi tải thành phố.
  - **More Vehicles & More PathUnits x2:** Đồng bộ 65,536 phương tiện và 524,288 path units.

---

### ⚡ Ưu điểm vượt trội & Tính năng chính

1. **Mở rộng sức chứa 64K thực thụ:**
   - Nâng giới hạn node và segment lên 65,536 (chuẩn 64K).
   - Thoải mái phát triển hệ thống cao tốc đa tầng, mạng lưới đường sắt liên vùng và tàu điện ngầm dày đặc.

2. **Khắc phục triệt để lỗi sập trên macOS:**
   - Sửa toàn bộ lỗi tràn bộ nhớ và crash loop trên máy Mac (cả chip Apple Silicon M1/M2/M3/M4 lẫn Intel).
   - Cơ chế Dynamic IL Transpiler tự động vượt qua các giới hạn kiểm tra cứng của `KianCommons` và game gốc.

3. **Tương thích toàn diện với TM:PE (Traffic Manager):**
   - **Đèn giao thông đếm giây (Timed Traffic Lights):** Hoạt động 100% trên các nút giao mở rộng (> 32,768).
   - **Phân làn & Kết nối:** Bảo toàn cấu hình mũi tên chỉ làn, kết nối làn xe, tốc độ giới hạn và biển báo ưu tiên.
   - **Không lỗi Deserialization:** Dữ liệu cấu hình đèn tín hiệu được lưu và tải lại hoàn hảo khi vào lại game.

4. **Mô phỏng giao thông & Bản đồ nhiệt chính xác:**
   - Cập nhật thời gian thực màu sắc lưu lượng giao thông (Traffic Flow Color Map) trên các đoạn đường mở rộng.
   - Cơ chế quét mô phỏng chủ động loại bỏ hoàn toàn lỗi "xe đóng băng" / kẹt xe ảo tại các ngã tư ID cao.

5. **Bảo toàn dữ liệu Savegame an toàn:**
   - Lưu trữ tuần tự sạch sẽ, không gây hỏng file save hay mất dữ liệu hạ tầng khi tải lại thành phố.

6. **Tất cả trong một (Standalone):**
   - Bản mod hoàn chỉnh độc lập, không cần cài thêm các bản fix phụ trợ rời rạc.

---

### 🧩 Danh sách tương thích

Đã kiểm thử và tương thích mượt mà với hệ sinh thái mod hiện đại:
- **Harmony 2.2+** *(Bắt buộc cài đặt)*
- **TM:PE (Traffic Manager: President Edition)**
- **81 Tiles 2**
- **Move It**
- **Node Controller Renewal**
- **Intersection Marking Tool (IMT)**
- **Network Multitool**
- **Adaptive Roads (ADR)**
- **Network Skins**

---

### 🚀 Hướng dẫn kích hoạt trong Game

1. Khởi động **Cities: Skylines** -> Vào **Quản lý nội dung (Content Manager)** -> **Mods**.
2. Tìm và **BẬT (ON)**: `More Network Capacity Renew by NgocDuy`.
3. Vào game và tải bản lưu thành phố bình thường!

---

### 💡 Khuyến nghị sử dụng

* **Sao lưu Savegame:** Luôn tạo bản sao lưu định kỳ cho các thành phố tâm huyết trước khi sử dụng các mod can thiệp giới hạn gốc.
* **Duy trì kích hoạt Mod:** Khi thành phố đã vượt ngưỡng mặc định (> 32,768 node/segment), bạn bắt buộc phải duy trì bật mod để game có thể đọc và quản lý dữ liệu mạng lưới mở rộng.

---

## 👤 Author & Support
* **Author:** [NgocDuy1426](https://github.com/NqocDuy1426)
* **Steam Workshop:** [More Network Capacity Renew (ID: 3818403598)](https://steamcommunity.com/sharedfiles/filedetails/?id=3818403598)
