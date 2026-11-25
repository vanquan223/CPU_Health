# CPU Health

Widget giám sát hệ thống dành cho Windows 10 và 11. Giao diện tiếng Việt, ghim trên cùng theo mặc định và có thể kéo bằng vùng tiêu đề.

Bấm nút **Thu gọn** ở thanh tiêu đề hoặc nhấn `Ctrl+M` để chuyển sang thanh nhỏ chỉ hiện biểu tượng và số liệu. Bấm lên thanh nhỏ để trở về chế độ đầy đủ. Giữ và kéo thanh nhỏ để đổi vị trí.

Thanh thu gọn mặc định có nền trong suốt. Nhấp chuột phải lên thanh và chọn **Nền trong suốt** để bật hoặc tắt. Biểu tượng và số liệu có viền tối để dễ đọc trên nền sáng.

Ở chế độ đầy đủ, kéo tay nắm ở góc dưới bên phải vào trong để thu nhỏ widget đến 75%. Kéo ra ngoài để trở về kích thước ban đầu.

## Mở widget

Mở `bin\CPU Health.exe`. Nếu cần biên dịch lại, chạy lệnh sau trong thư mục dự án. Build dùng trình biên dịch .NET Framework có sẵn trên Windows.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

## Các chỉ số

- **CPU** hiển thị mức sử dụng hiện tại và biểu đồ 60 giây gần nhất.
- **RAM** hiển thị phần trăm và dung lượng bộ nhớ vật lý đang dùng.
- **DISK** hiển thị phần trăm thời gian ổ đĩa vật lý hoạt động và tốc độ đọc/ghi. Bấm **Disk 0** hoặc tên ổ đang chọn để đổi ổ. Bấm **Chi tiết** để xem độ trễ trung bình, biểu đồ hoạt động và tốc độ truyền của 60 giây gần nhất, cùng dung lượng, phân vùng đã định dạng, ổ chứa Windows, page file và loại ổ khi Windows cung cấp dữ liệu.
- **Nhiệt độ** hiển thị nhiệt độ CPU khi có dữ liệu từ LibreHardwareMonitor qua WMI, hoặc nhiệt độ vùng Windows báo cáo qua ACPI. Tên nguồn hiển thị bên dưới chỉ số.

CPU, RAM và DISK cập nhật mỗi giây. Widget kiểm tra nhiệt độ mỗi 5 giây qua tác vụ nền. Nếu không đọc được cảm biến, ô nhiệt độ hiển thị `—`. Nguồn LibreHardwareMonitor hoạt động khi một phiên bản đang chạy trên máy xuất cảm biến qua WMI. [Tài liệu về nguồn WMI của LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/issues/2143).

Nếu Windows không cấp quyền đọc bộ đếm ổ vật lý, widget lấy số liệu từ các phân vùng đọc được và ghi **Ước tính từ phân vùng** trong trang chi tiết. Tốc độ đọc/ghi là tổng của các phân vùng đó. Phần trăm hoạt động lấy mức cao nhất giữa các phân vùng vì thời gian I/O có thể chồng lấp. Dung lượng đã định dạng là tổng dung lượng các phân vùng đọc được mà Windows nhận dạng hệ thống tệp.
