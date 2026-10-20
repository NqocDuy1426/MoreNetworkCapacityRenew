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

### 🚨 What's New in v4.3.40 (Full-Map TM:PE Traffic Light Stepper & 100% Zero-Hook Architecture)
- **Full-Map TM:PE Traffic Light Stepper (Fixed Frozen Vehicles):**
  - Solved the root cause of vehicles standing completely still mid-road or at intersections despite having a valid route and destination: TM:PE's native `SimulationStep` hardcoded simulation stepping to nodes `0..32,767`. Nodes `32,768..65,023` were never stepped, causing traffic lights on those nodes to remain perpetually stuck in red/unadvanced phases.
  - Added native `MNCTrafficLightStepper` (`ICities.ThreadingExtensionBase`) that runs on the simulation thread, natively stepping nodes `32,768..65,023` (504 nodes per frame) with zero overhead via JIT dynamic invocation (`ldelema` on struct array).
  - Timed traffic lights and adaptive signals across the entire 64K map now cycle fluidly.
- **Strictly Zero Harmony Hooks into TM:PE (Full Performance & FPS Restored):**
  - Completely eliminated all intrusive Harmony hooks into TM:PE methods (`SegmentEndManager.GetIndex`, `TurnOnRedManager.GetIndex`, `TrafficMeasurementManager.GetDirIndex`, `GlobalConfig`, etc.). TM:PE runs 100% pure native code.
  - Excluded `TrafficManager` from `DynamicPatcher.targetModNames` to preserve native enum values (`ExtVehicleType.CableCar`) and retain full Mono JIT optimization, resolving severe FPS degradation.
  - All TM:PE data synchronization is accomplished safely via direct memory buffers (`_segBuffer`, `_nodeBuffer`, `_laneBuffer`) and expanded array capacities.
- **Contiguous 131,072 Buffer Expansion for TM:PE Split Arrays:**
  - Expanded `TurnOnRedSegments`, `SegmentDirTrafficData`, and `SegmentEnds` contiguously to 131,072 elements. TM:PE's native indexing (`segmentId + 36864`, max 101,888) fits comfortably without any hooks or shifting.
- **Anti-Freeze Speed Limit Cache Guarantee:**
  - Ensured all entries in TM:PE's speed limit cache (`cachedLaneSpeedLimits_`) <= 0.001f default to `1.25f` (50 km/h), completely preventing vehicles from stopping dead mid-lane due to zero-speed cache entries.
- **Safe Pulse Unit Casting:**
  - Wrapped `SafeCopyPulseUnits` in safe exception handling to prevent `InvalidCastException` with mods like 81 Tiles 2.

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

### 🚨 Điểm mới trong bản cập nhật v4.3.40 (Bộ quét Đèn Giao thông Toàn bản đồ & Kiến trúc Không Hook TM:PE)
- **Bộ quét Đèn Tín hiệu Toàn bản đồ TM:PE (Khắc phục Triệt để Xe đứng im giữa đường):**
  - Tìm ra nguyên nhân gốc rễ hiện tượng xe đứng im giữa đường/nút giao dù có điểm đến và lộ trình hợp lệ: Hàm `SimulationStep` gốc của TM:PE chỉ quét và nhảy pha đèn cho các node `0..32,767`. Các node mở rộng `32,768..65,023` hoàn toàn không bao giờ được TM:PE quét, khiến đèn tín hiệu tại các node này luôn kẹt cứng ở màu đỏ vĩnh viễn, xe dừng lại chờ đèn chuyển xanh nhưng đèn không bao giờ nhảy.
  - Bổ sung `MNCTrafficLightStepper` (`ICities.ThreadingExtensionBase`) chạy trực tiếp trên luồng mô phỏng, quét định kỳ các node `32,768..65,023` (504 node mỗi frame) với tốc độ JIT Dynamic invocation (`ldelema`) siêu tốc (< 1 microsecond/frame).
  - Đèn đếm giây và chu kỳ chuyển pha đèn hoạt động mượt mà 100% trên toàn bộ bản đồ 64K.
- **Tuyệt đối Không Can thiệp Hook Harmony vào các hàm của TM:PE (Khôi phục FPS & Hiệu năng Cao):**
  - Loại bỏ hoàn toàn toàn bộ các hook can thiệp vào TM:PE (`SegmentEndManager.GetIndex`, `TurnOnRedManager.GetIndex`, `TrafficMeasurementManager.GetDirIndex`, `GlobalConfig`, v.v.). TM:PE chạy 100% native nguyên bản.
  - Gỡ bỏ `TrafficManager` khỏi `DynamicPatcher.targetModNames` để bảo toàn enum `ExtVehicleType.CableCar` và giữ nguyên tối ưu hóa của Mono JIT, khắc phục triệt để tình trạng tụt FPS nghiêm trọng.
  - Đồng bộ dữ liệu TM:PE an toàn thông qua con trỏ bộ nhớ trực tiếp (`_segBuffer`, `_nodeBuffer`, `_laneBuffer`) và mở rộng dung lượng mảng.
- **Mở rộng Mảng Đôi Contiguous 131,072 cho TM:PE:**
  - Mở rộng các mảng `TurnOnRedSegments`, `SegmentDirTrafficData`, và `SegmentEnds` lên 131,072 phần tử liên tục. Công thức đánh chỉ mục gốc của TM:PE (`segmentId + 36864`, tối đa 101,888) hoạt động tự nhiên không cần hook dịch chuyển chỉ số.
- **Chống Đóng băng Tốc độ (Anti-Freeze Speed Limit Cache Guarantee):**
  - Đảm bảo toàn bộ các slot trong cache tốc độ TM:PE (`cachedLaneSpeedLimits_`) <= 0.001f được gán mặc định `1.25f` (50 km/h), ngăn chặn hoàn toàn hiện tượng xe bị gán tốc độ 0 km/h đứng chôn chân.
- **Bảo vệ Ép kiểu Đơn vị Nước/Xung (Safe Pulse Unit Casting):**
  - Bọc try-catch an toàn trong `SafeCopyPulseUnits` loại bỏ hoàn toàn lỗi `InvalidCastException` khi dùng chung với 81 Tiles 2.

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
