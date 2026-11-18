# ADR-002: Mã trạng thái cho redirect (301 hay 302)

## Status

Proposed (bản nháp, cần chủ repo đọc lại và chuyển sang Accepted)

## Context

GET /{code} trả về một redirect tới target URL. HTTP có bốn mã redirect liên quan:

- 301 Moved Permanently: vĩnh viễn; trình duyệt và proxy được cache mặc định, lần sau trình duyệt có thể đi thẳng tới đích mà không hỏi lại server.
- 302 Found: tạm thời; trình duyệt mặc định không cache, mỗi lần click đều đi qua server.
- 307 Temporary Redirect và 308 Permanent Redirect: giống 302 và 301 nhưng bắt buộc giữ nguyên method và body. Với redirect bằng GET thì không khác biệt thực tế.

Hai yêu cầu của project bị ảnh hưởng trực tiếp:

- US-07: chủ link được đổi target hoặc disable link, và thay đổi phải có hiệu lực.
- US-11: đếm click. Click nào không tới server thì không đếm được.
- Ngoài ra benchmark (US-04, US-10) đo tải redirect lên server; nếu trình duyệt tự cache thì không phản ánh tải thật.

## Decision

Trả 302 Found cho mọi redirect thành công. 404 khi code không tồn tại, 410 Gone khi link đã disabled hoặc hết hạn.

Response 302 không đặt Cache-Control cho phép cache (để mặc định, hoặc đặt Cache-Control: private, no-cache nếu sau này đo thấy proxy nào đó cache).

## Alternatives Considered

### Option 1: 301 Moved Permanently

- Ưu điểm: giảm tải server và latency cho người dùng quay lại vì trình duyệt tự đi thẳng; SEO coi link đích là chính.
- Nhược điểm: trình duyệt có thể cache rất lâu và không có cách xoá từ phía server. Đổi target hay disable link thì người đã từng click vẫn đi tới đích cũ. Click lặp lại không tới server nên thống kê thiếu. Sai với yêu cầu quản lý link của project.

### Option 2: 302 Found (chọn)

- Ưu điểm: mọi click đi qua server nên đổi target, disable, expire có hiệu lực ngay; thống kê đầy đủ; benchmark đo đúng tải thật.
- Nhược điểm: mọi click tốn một round trip tới server, tải đọc lên server cao hơn. Chính tải đọc này là lý do project cần cache ở phía server (Redis) thay vì nhờ cache trình duyệt.

### Option 3: 307 / 308

- Ưu điểm: ngữ nghĩa chặt hơn về giữ method.
- Nhược điểm: redirect của shortener luôn là GET nên không được gì thêm; 302 phổ biến và quen thuộc hơn với mọi client.

## Consequences

### Positive
- Đúng với US-07 và US-11.
- Kết quả benchmark phản ánh tải thật lên API, SQL và sau này Redis.
- 410 phân biệt rõ link từng tồn tại nhưng đã tắt với link chưa từng có (404).

### Negative
- Tải redirect hoàn toàn dồn lên server, cần cache phía server để giảm tải DB (đúng là bài toán của v0.2).

## Ghi chú cho phỏng vấn

Các shortener lớn chọn tuỳ mục đích: dịch vụ cần analytics và cho đổi đích thường dùng 302 (hoặc 301 kèm Cache-Control ngắn); dịch vụ chỉ cần chuyển hướng vĩnh viễn thì dùng 301 để tiết kiệm tải. Không có đáp án đúng chung, chỉ có đáp án đúng với yêu cầu.

## Câu hỏi tự kiểm tra trước khi Accepted

- Nếu lỡ trả 301 cho một link rồi mới đổi target, có cách nào sửa cho người dùng đã click chưa?
- CDN có cache được 302 không? Cần header gì?
- Vì sao link hết hạn trả 410 mà không phải 404? Client (hoặc crawler) xử lý hai mã này khác nhau thế nào?

## Related Documents

- docs/03-api-contract.md
- Requirement US-02, mục 11 ADR-002
