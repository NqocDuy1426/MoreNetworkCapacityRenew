# More Nodes/Segments v5.0

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

**More Nodes/Segments v5.0** (formerly *More Network Capacity Renew*) safely elevates this internal engine ceiling to **65,536 (64K) Nodes and Segments**, providing double the building freedom. 

Unlike previous experimental or unmaintained releases that suffered from severe crash loops and broken traffic routing, **More Nodes/Segments** is engineered from the ground up for **rock-solid stability, accurate traffic simulation, and zero savegame data loss.**

---

### 🚨 What's New in v5.0 & v5.0.1 (More Nodes/Segments Major Release & Hotfixes)
- **v5.0.1 Hotfix (Impatient Commuters Compatibility & Zero-Hook Architecture):**
  - **Solved Simulation Thread Freeze:** Resolved `IndexOutOfRangeException` when running with *Impatient Commuters* (Workshop ID `3721119397`). The mod hardcoded its stop node tracking arrays to 32,768, crashing simulation threads when public transit stops were placed on nodes >= 32,768.
  - **Pure In-Memory Array Expansion:** Automatically sizes all 5 limiter arrays to 65,536 elements (`ushort.MaxValue + 1`) directly in memory on level load and simulation ticks.
  - **Strictly Zero Harmony Hooks:** No methods in third-party mods are hooked, preserving 100% of the mod's native logic and performance.
- **v5.0.0 Major Release & Official Rebranding:**
  - Officially rebranded from *More Network Capacity Renew* to **More Nodes/Segments v5.0**!
  - **Thread-Safe Save & Quit Armor:** Eliminated off-thread `QuitApplication()` call during save completion, safely delegating to the Main Thread via `ThreadHelper.dispatcher.Dispatch`. Completely prevents Unity UI `NullReferenceException` in Adaptive Roads and rendering engines when saving and quitting.
  - **Water Transport & Ship Path Line Fix:** Configures dual `Ship | Ferry` vehicle types on water networks, expands dock search connection radius to 128m with automatic fallback, and enables passenger Harbors to serve as transit stops.
- **Full-Map TM:PE Traffic Light Stepper (Fixed Frozen Vehicles):**
  - `MNCTrafficLightStepper` (`ICities.ThreadingExtensionBase`) natively steps nodes `32,768..65,023` (504 nodes per frame) with zero overhead via JIT dynamic invocation, ensuring timed traffic lights cycle fluidly across the entire 64K map.
- **Strictly Zero Harmony Hooks into TM:PE (Full Performance & FPS Restored):**
  - TM:PE runs 100% pure native code. All data synchronization is achieved safely via direct memory buffers (`_segBuffer`, `_nodeBuffer`, `_laneBuffer`).

---

### ⚡ Key Advantages & Superior Features

1. **True 64K Expanded Capacity:**
   - Doubles maximum node and segment capacity to 65,536 (and lanes to 524,288).
   - Build extensive regional passenger & cargo rail, complex multi-level highway interchanges, and expansive subway grids without restriction.

2. **Native Apple Silicon & Intel Mac Stability:**
   - Eliminates hard memory alignment overflows and engine crash loops on macOS (M1/M2/M3/M4 & Intel) and Windows.
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
- **Impatient Commuters** *(Auto-expanded to 65K stop nodes, zero-hook)*
- **81 Tiles 2**
- **Move It**
- **Node Controller Renewal**
- **Intersection Marking Tool (IMT)**
- **Network Multitool**
- **Adaptive Roads (ADR)**
- **Network Skins 2**
- **Network Anarchy**
- **Picker & Find It 2**
- **Advanced Road Tools**
- **Improved Public Transport 2 & 3**
- **Advanced Vehicle Options**
- **More CitizenUnits & More PathUnits** (Universal Dynamic Buffer Guard)
- **Universal Mod Harmonizer** (Tự động đồng bộ và mở rộng bộ nhớ cho mọi mod cộng đồng)

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
The script compiles the assembly with `Mono` / `mcs`, strips redundant interfaces, and deploys directly to your game's `Addons/Mods/MoreNodesSegments` and Workshop directories.

---

### 💡 Best Practices & Safety

* **Backup Your Saves:** Always maintain regular backup copies of your savegames before testing limit-breaking mods.
* **Persistent Saves:** Once a city exceeds vanilla network limits (> 32,768 nodes/segments), this mod must remain enabled for the game to properly load and manage those extended networks.

---
---

## 🇻🇳 Tiếng Việt

### Giới thiệu: Bản mod này là gì?
Mặc định *Cities: Skylines* giới hạn số lượng hạ tầng mạng lưới đường sá ở mức ~32,000 node và segment. Khi chạm ngưỡng này, trò chơi sẽ thông báo lỗi `"Cannot create more objects of this type"`, khiến người chơi không thể đặt thêm đường xá hay đường sắt.

**More Nodes/Segments v5.0** (tên cũ: *More Network Capacity Renew*) phá vỡ rào cản này một cách an toàn, nâng tổng sức chứa lên **65,536 (64K) Node & Segment** - nhân đôi không gian xây dựng để bạn thỏa sức kiến thiết các siêu đô thị triệu dân.

Khác biệt hoàn toàn so với các bản thử nghiệm cũ dễ gây sập game và lỗi mô phỏng giao thông, phiên bản **More Nodes/Segments** được xây dựng lại toàn diện với tiêu chí: **độ ổn định tuyệt đối, mô phỏng giao thông chuẩn xác và không mất dữ liệu savegame.**

---

### 🚨 Điểm mới trong bản cập nhật v5.0 & v5.0.1 (Phát hành Lớn & Bản vá Tương thích)
- **v5.0.1 Hotfix (Tương thích Hoàn hảo Impatient Commuters & Kiến trúc Zero-Hook):**
  - **Khắc phục Triệt để Lỗi Đóng băng Luồng Mô phỏng:** Sửa lỗi `IndexOutOfRangeException` khi chơi cùng mod *Impatient Commuters* (Workshop ID `3721119397`). Mod này cố định kích thước mảng trạm dừng ở 32,768 phần tử, khiến game bị treo luồng tính toán khi có trạm dừng đặt tại node >= 32,768.
  - **Mở rộng Mảng Trực tiếp trong Bộ nhớ (Pure In-Memory Expansion):** Tự động mở rộng toàn bộ 5 mảng nội bộ của limiter lên 65,536 phần tử (`ushort.MaxValue + 1`) trực tiếp trong bộ nhớ khi load màn và tick mô phỏng.
  - **Tuyệt đối Không Hook Harmony vào Mod Khác:** Không can thiệp, không hook vào bất kỳ hàm nào của bên thứ ba, bảo toàn 100% logic nguyên bản và hiệu năng.
- **v5.0.0 Bản Phát hành Lớn & Đổi tên Chính thức:**
  - Chính thức đổi tên từ *More Network Capacity Renew* thành **More Nodes/Segments v5.0**!
  - **Cơ chế Lưu & Thoát Game An toàn (Thread-Safe Save & Quit Armor):** Khắc phục lỗi gọi `QuitApplication()` ngoài luồng phụ khi hoàn tất lưu game, chuyển giao an toàn về Main Thread qua `ThreadHelper.dispatcher.Dispatch`. Triệt tiêu hoàn toàn lỗi Unity UI `NullReferenceException` trong Adaptive Roads và engine hiển thị khi Lưu & Thoát.
  - **Hỗ trợ Tuyến Đường Thủy & Luồng Tàu (Water Transport & Ship Path Fix):** Cấu hình tự động dạng phương tiện đôi `Ship | Ferry` trên mạng lưới đường thủy, nới rộng bán kính tìm kiếm bến cảng lên 128m kèm cơ chế dự phòng tự động, cho phép các bến tàu chở khách hoạt động như trạm dừng trung chuyển.
- **Bộ quét Đèn Tín hiệu Toàn bản đồ TM:PE (Khắc phục Triệt để Xe đứng im giữa đường):**
  - `MNCTrafficLightStepper` (`ICities.ThreadingExtensionBase`) chạy trực tiếp trên luồng mô phỏng, quét định kỳ các node `32,768..65,023` (504 node mỗi frame) với tốc độ JIT Dynamic invocation (`ldelema`) siêu tốc (< 1 microsecond/frame).
- **Tuyệt đối Không Can thiệp Hook Harmony vào các hàm của TM:PE (Khôi phục FPS & Hiệu năng Cao):**
  - TM:PE chạy 100% native nguyên bản. Mọi dữ liệu được đồng bộ an toàn qua các buffer bộ nhớ trực tiếp (`_segBuffer`, `_nodeBuffer`, `_laneBuffer`).

---

### ⚡ Ưu điểm vượt trội & Tính năng chính

1. **Mở rộng sức chứa 64K thực thụ:**
   - Nâng giới hạn node và segment lên 65,536 (chuẩn 64K) và làn đường lên 524,288.
   - Thoải mái phát triển hệ thống cao tốc đa tầng, mạng lưới đường sắt liên vùng và tàu điện ngầm dày đặc.

2. **Khắc phục triệt để lỗi sập trên macOS:**
   - Sửa toàn bộ lỗi tràn bộ nhớ và crash loop trên máy Mac (cả chip Apple Silicon M1/M2/M3/M4 lẫn Intel) và Windows.
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
- **Impatient Commuters** *(Tự động mở rộng mảng trạm dừng lên 65K, zero-hook)*
- **81 Tiles 2**
- **Move It**
- **Node Controller Renewal**
- **Intersection Marking Tool (IMT)**
- **Network Multitool**
- **Adaptive Roads (ADR)**
- **Network Skins 2**
- **Network Anarchy**
- **Picker & Find It 2**
- **Advanced Road Tools**
- **Improved Public Transport 2 & 3**
- **Advanced Vehicle Options**
- **More CitizenUnits & More PathUnits** (Universal Dynamic Buffer Guard)
- **Universal Mod Harmonizer** (Tự động đồng bộ và mở rộng bộ nhớ cho mọi mod cộng đồng)

---

### 🚀 Hướng dẫn kích hoạt trong Game

1. Khởi động **Cities: Skylines** -> Vào **Quản lý nội dung (Content Manager)** -> **Mods**.
2. Tìm và **BẬT (ON)**: `More Nodes/Segments v5.0 by NgocDuy`.
3. Vào game và tải bản lưu thành phố bình thường!

---

### 💡 Khuyến nghị sử dụng

* **Sao lưu Savegame:** Luôn tạo bản sao lưu định kỳ cho các thành phố tâm huyết trước khi sử dụng các mod can thiệp giới hạn gốc.
* **Duy trì kích hoạt Mod:** Khi thành phố đã vượt ngưỡng mặc định (> 32,768 node/segment), bạn bắt buộc phải duy trì bật mod để game có thể đọc và quản lý dữ liệu mạng lưới mở rộng.

---

## 👤 Author & Support
* **Author:** [NgocDuy1426](https://github.com/NqocDuy1426)
* **Steam Workshop:** [More Nodes/Segments (ID: 3818403598)](https://steamcommunity.com/sharedfiles/filedetails/?id=3818403598)
* **GitHub Repository:** [MoreNodesSegments](https://github.com/NqocDuy1426/MoreNodesSegments)
