# ADR-004: Xoá cache và giới hạn dữ liệu cũ

## Status

Proposed (bản nháp, cần chủ repo đọc lại và chuyển sang Accepted)

## Context

Chủ link có thể đổi target, đổi expiry hoặc disable link (US-03). Sau khi đổi, redirect phải theo dữ liệu mới. Nhất là sau khi disable, link không được tiếp tục redirect từ cache (US-07).

Hai câu hỏi:
- Khi update thì xoá key hay ghi đè giá trị mới vào cache?
- Chấp nhận dữ liệu cũ tối đa bao lâu?

## Decision

Commit SQL trước, sau đó xoá key (DEL link:v2:{code}). TTL tối đa 1 giờ là lưới an toàn.

- Code: LinkService.UpdateAsync và DisableAsync gọi InvalidateAsync sau SaveChanges.
- Nếu DEL thất bại (Redis lỗi), request vẫn thành công và chỉ log Warning. Entry cũ sống tối đa tới hết TTL.
- Disable gọi lại lần nữa cũng xoá key lại. Lần gọi thứ hai sửa được một lần DEL trước đó bị lỗi.
- Giới hạn dữ liệu cũ chấp nhận được là 1 giờ, và chỉ xảy ra khi DEL lỗi. Lý do: đây là link rút gọn, không phải dữ liệu tiền. Disable một link xấu mà trong trường hợp tệ nhất phải chờ một giờ mới có hiệu lực là chấp nhận được ở quy mô này. Nếu cần chặt hơn thì giảm Cache:LinkTtl, đổi lại hit ratio giảm.

Có test: CacheAsideTests kiểm tra cache rồi PATCH thì target mới có hiệu lực, cache rồi disable thì trả 410, DEL lỗi thì PATCH vẫn trả 200. RedisOutageTests kiểm tra PATCH thành công khi Redis tắt.

## Alternatives Considered

### Option 1: Ghi đè giá trị mới thay vì xoá

- Ưu điểm: lần đọc tiếp theo hit luôn.
- Nhược điểm: hai request update đồng thời có thể ghi vào cache theo thứ tự ngược với thứ tự commit vào DB, và cache giữ giá trị cũ tới hết TTL. Xoá thì không có vấn đề thứ tự: lần đọc sau luôn lấy từ DB.

### Option 2: Xoá key trước rồi mới commit

- Nhược điểm: một redirect chen vào giữa lúc xoá và lúc commit sẽ đọc dòng cũ từ DB rồi ghi lại vào cache. Vì vậy phải commit trước.

### Option 3: Versioned value (so updated_at)

Value có trường u (updated_at). Có thể từ chối ghi đè bằng bản cũ hơn.

- Nhược điểm: phải dùng script Lua hoặc WATCH để so rồi ghi một cách nguyên tử. Hiện chưa cần; trường u đã có sẵn trong value để sau này dùng.

### Option 4: Delayed double delete

Xoá, chờ vài trăm ms, rồi xoá lần nữa để bắt race giữa lúc đọc và lúc ghi cache.

- Nhược điểm: phức tạp, và độ trễ phải chọn theo kinh nghiệm. Race hiếm gặp ở traffic này, và TTL đã giới hạn thiệt hại. Để giai đoạn 2.

## Consequences

### Positive
- Đơn giản. Bình thường không có dữ liệu cũ. Khi Redis lỗi thì có giới hạn trên rõ ràng (TTL).

### Negative
- Còn một race nhỏ: một miss đọc dòng cũ trước khi commit, rồi ghi vào cache sau khi đã xoá. Chưa tái hiện, TTL giới hạn thiệt hại.
- Cache Memory (per-instance) chỉ xoá được trên chính instance đó.

## Related Documents

- docs/adr/ADR-003-caching-pattern.md
- docs/02-database-design.md
