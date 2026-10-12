# More Network Capacity Renew by NgocDuy

Bản mod hợp nhất hoàn chỉnh độc lập giữa **MoreNetworkCapacity** (mở rộng giới hạn 64K node/segment) và toàn bộ các bản sửa lỗi toàn diện (**MoreNetworkCapacityFix**).

- **Steam Workshop:** [More Network Capacity Renew (ID: 3818403598)](https://steamcommunity.com/sharedfiles/filedetails/?id=3818403598)
- **Author:** NgocDuy1426

---

## 🌟 Tính năng hợp nhất trong bản Renew
1. **Mở rộng dung lượng mạng tối đa 64K**:
   - Mở rộng Node & Segment lên 65,024 (chuẩn 64K).
   - Tương thích 100% với các savegame cũ có gắn cờ metadata `User/MoreNetworkCapacity/64K-v1`.
2. **Khắc phục triệt để lỗi Mac OS**:
   - Vượt qua giới hạn kiểm tra cứng của KianCommons (`IsNodeValid`, `IsSegmentValid`, `AssertSegmentValid`).
   - Dynamic IL Transpiler tự động mở rộng các điều kiện biên của `Assembly-CSharp` và các mod giao thông.
3. **Đồng bộ hóa & tương thích TM:PE toàn diện**:
   - Tự động đồng bộ và mở rộng các buffer (`_segBuffer`, `_nodeBuffer`, `_laneBuffer`, `_buildingBuffer`, `_vehicleBuffer`, `_parkedVehiclesBuffer`).
   - Tự động mở rộng các mảng quản lý của TM:PE (`parkingAllowed`, `SegmentEndManager`, `TurnOnRedManager`, `TrafficMeasurementManager`, `RoutingManager`).
   - Tự động sinh `DynamicMethod` JIT delegate chạy mô phỏng đèn giao thông TM:PE cho các node mở rộng từ 32,768 đến 65,024 mượt mà, không bị lag.
4. **Mô phỏng vanilla cho các node & segment mở rộng**:
   - Mô phỏng chu kỳ 256 frame chuẩn Cities: Skylines cho node 32,768–65,023 và segment 36,864–65,023.
   - Làm mượt lưu lượng xe và hiển thị màu sắc lưu lượng giao thông (Traffic Flow Color Map) chuẩn xác 100%.
5. **Dọn dẹp & phục hồi dữ liệu tự động**:
   - Khử vòng lặp vô tận của xe (`SanitizeVehicleGrid`).
   - Dọn dẹp cờ nghẽn bất thường (`SanitizeNetwork`: reset `m_trafficBuffer == 65535` và gỡ cờ `NetSegment.Flags.Blocked`).
   - Mở rộng các mảng va chạm của `ToolController` (`m_collidingSegments`, `m_collidingBuildings`) lên 1,024 bitmask (hỗ trợ 65,536).

---

## 🛠️ Cấu trúc thư mục
- `Renew.cs`: Mã nguồn C# hợp nhất chứa `RenewMod : IUserMod`, `RenewLoading : LoadingExtensionBase`, cùng toàn bộ các patch sửa lỗi.
- `strip_iusermod.cs`: Công cụ tự động tước interface `IUserMod` từ `MoreNetworkCapacity.dll` gốc để game chỉ hiển thị duy nhất 1 mod trong Content Manager.
- `MoreNetworkCapacity.Core.dll`: Engine 64K gốc đã được tối ưu hóa interface.
- `MoreNetworkCapacityRenew.dll`: Thư viện mod chính đã tích hợp tài nguyên.
- `build.sh`: Kịch bản build 1-click tự động biên dịch và cài đặt vào thư mục game.

---

## 🚀 Hướng dẫn kích hoạt trong Game
1. Mở **Cities: Skylines** -> Vào mục **Content Manager** -> **Mods**.
2. Tìm và **BẬT (ON)**:
   - `More Network Capacity Renew by NgocDuy`
3. **TẮT (OFF)** hoặc gỡ bỏ:
   - `More Network Capacity` (bản gốc)
   - `More Network Capacity Fix` (bản fix rời trước đó)
4. Vào game và tải thành phố bình thường!
