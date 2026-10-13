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

### 🚨 What's New in v2.2 (Speed Limit Deadstop Fix & Performance Boost)
- **Highway & Straight Road Deadstop Fix (Speed Limit Zero Bug):** Resolved the issue where vehicles abruptly braked or stopped dead on straight highway and avenue sections when TM:PE speed limits were active. Pre-initialized all 524,288 TM:PE lane speed limit cache slots with road default speeds (preventing unpopulated high-ID lanes from defaulting to 0 km/h) and established an active runtime fallback shield so vehicles never receive a 0 speed limit.
- **Major FPS & Simulation Performance Optimization:**
  - **Eliminated render group rebuild bottlenecks:** Removed redundant renderer update calls from simulation ticks that were invalidating render groups and causing micro-stutter / FPS drops.
  - **Simulation frame gating:** Network simulation for expanded nodes/segments now strictly runs once per simulation frame index, preventing duplicate processing across sub-steps.
  - **Zero-redundancy color updates:** Color updates now only process expanded elements (36,864+ segments and 32,768+ nodes) instead of re-scanning all 65K elements repeatedly.
  - **TM:PE Traffic Light CPU optimization:** Traffic light simulation steps now skip uncreated nodes immediately, cutting CPU cycles by ~95%.
- **Memory & Garbage Collection Optimizer (`MemoryOptimizer`):** Resolves the critical `"Fatal error in gc: Too many heap sections"` (Unity 5.6 Mono GC breakpoint 0x80000003) crash. Automatically mitigates heap fragmentation when loading large cities with heavy asset counts and Procedural Objects (PO Mod).
- **Reload & Exit-to-Menu Crash Fix:** Eliminated crashes and freezes when exiting to the main menu or reloading a city. Unsafe finalizer synchronization has been replaced with safe lifecycle teardown.

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

### 🚨 Điểm mới trong bản cập nhật v2.2 (Sửa lỗi Dừng xe trên Cao tốc & Tăng vọt Hiệu năng)
- **Khắc phục triệt để lỗi xe dừng khựng trên cao tốc & đường thẳng (Lỗi Vận tốc = 0 TM:PE):** Sửa lỗi xe ô tô tự động phanh gấp hoặc dừng chết trên các đoạn đường thẳng tắp/đại lộ khi bật tính năng Speed Limit của TM:PE. Tự động khởi tạo giá trị vận tốc mặc định cho toàn bộ 524,288 làn đường trong bộ nhớ cache của TM:PE (tránh việc các làn ID cao bị gán nhầm tốc độ 0 km/h) kết hợp cơ chế bảo vệ dự phòng theo thời gian thực (fallback shield) để xe không bao giờ nhận vận tốc 0.
- **Tối ưu hóa Hiệu năng & Khắc phục triệt để Tụt FPS:**
  - **Loại bỏ hiện tượng nghẽn mesh render:** Gỡ bỏ các lệnh cập nhật renderer không cần thiết trong vòng lặp mô phỏng mỗi tick – nguyên nhân chính gây giật lag và buộc card đồ họa phải tính toán lại render mesh liên tục.
  - **Đồng bộ chu kỳ mô phỏng (Frame Gating):** Quy trình mô phỏng các node & segment mở rộng chỉ chạy chính xác 1 lần mỗi khung hình (frame index), loại bỏ hoàn toàn các chu kỳ mô phỏng trùng lặp qua từng substep.
  - **Tối ưu làm mới màu sắc (Zero-redundancy Color Update):** Lệnh cập nhật màu sắc chỉ quét các phần tử mở rộng (36,864+ segment và 32,768+ node) thay vì quét trùng lặp toàn bộ 65K phần tử như trước.
  - **Tối ưu hóa CPU cho Đèn giao thông TM:PE:** Bỏ qua ngay lập tức các nút chưa khởi tạo (uncreated nodes), giảm tới ~95% tài nguyên CPU tiêu thụ trong các bước tính toán đèn giao thông.
- **Tối ưu hóa Bộ nhớ & Bộ thu gom rác (`MemoryOptimizer`):** Khắc phục triệt để lỗi sập game `"Fatal error in gc: Too many heap sections"` (Unity 5.6 Mono GC breakpoint 0x80000003) khi tải thành phố lớn có nhiều tài nguyên (Assets) hoặc dùng mod Procedural Objects (PO).
- **Khắc phục lỗi văng game khi Thoát ra Menu / Tải lại Save:** Loại bỏ hoàn toàn lỗi crash khi quay lại menu chính hoặc nạp lại bản lưu, đảm bảo giải phóng bộ nhớ an toàn.

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
