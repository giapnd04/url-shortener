# ADR-007: Kiến trúc 2 project và Minimal API

## Status

Accepted

## Context

OrderFlow dùng Clean Architecture 4 lớp (Domain, Application, Infrastructure, API) và controller, vì domain dày: vòng đời đơn hàng, tồn kho, thanh toán, transaction nhiều bảng.

URL Shortener khác hẳn:

- Domain rất mỏng: một link có code, target, expiry, trạng thái. Gần như không có quy tắc nghiệp vụ phức tạp.
- Trọng tâm là luồng đọc và hạ tầng: SQL, Redis, timeout, đo lường.
- Khoảng 8 endpoint.

Số ADR 001 đến 006 đã được requirement dành cho các quyết định về code, redirect, cache, invalidation, Redis lỗi và analytics, nên ADR này lấy số 007.

## Decision

- Hai project: UrlShortener.Api (endpoint, validation, cấu hình) và UrlShortener.Infrastructure (EF Core, sau này Redis). Không có project Domain và Application riêng.
- Minimal API, gom endpoint theo nhóm bằng MapGroup và tách mỗi nhóm thành một file extension method (ví dụ LinkEndpoints, RedirectEndpoints) để Program.cs không phình.
- Không dùng MediatR hay pipeline behavior.
- Đọc dữ liệu qua một interface nhỏ (ví dụ ILinkReader) để từ v0.2 bọc bằng decorator cache mà không sửa endpoint. Đây là chỗ duy nhất cần trừu tượng hoá, vì nó là điểm sẽ thay đổi.

## Alternatives Considered

### Option 1: Clean Architecture 4 lớp như OrderFlow

- Ưu điểm: quen tay, ranh giới rõ.
- Nhược điểm: phần lớn file chỉ chuyển tiếp dữ liệu qua các lớp vì domain gần như rỗng; tăng số file mà không giảm độ phức tạp thật.

### Option 2: Controller

- Ưu điểm: giống OrderFlow, nhiều tài liệu.
- Nhược điểm: không học thêm được gì; với ít endpoint, controller thêm cấu trúc không cần thiết.

### Option 3: 2 project + Minimal API (chọn)

- Ưu điểm: ít lớp, đọc nhanh; hạ tầng vẫn tách riêng nên test và thay thế được; học thêm Minimal API, route group, endpoint filter.
- Nhược điểm: nếu domain lớn lên (người dùng, gói cước, custom domain) thì phải tách thêm lớp; Minimal API cần tự tổ chức file vì không có quy ước sẵn như controller.

## Consequences

### Positive
- Chọn kiến trúc khác nhau cho hai project cho thấy chọn theo bài toán, không theo thói quen. Đây là câu trả lời khi bị hỏi vì sao hai project trong CV khác nhau.
- Chỗ đổi nhiều nhất (đọc link, cache) có đúng một interface để chèn decorator.

### Negative
- Khi mở rộng phạm vi cần xem lại ADR này.

## Related Documents

- docs/01-project-definition.md
- Requirement mục 8 và mục 11
